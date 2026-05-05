using System.Diagnostics;
using System.Threading.Tasks;
using ImageLocker.Models;

namespace ImageLocker.Services;

/// <summary>
/// Searches for good payload embedding positions with a genetic algorithm.
/// The objective is to reduce visible changes, which improves MSE and PSNR.
/// </summary>
public class GeneticAlgorithmService
{
    // Small tolerance for comparing nearly identical PSNR values to prevent floating-point differences from affecting ranking.
    private const double Epsilon = 0.001;

    // Stops the GA early if the best solution stagnates for this many generations, saving time when convergence is reached.
    private const int MaxStagnantGenerations = 2;

    private readonly MetricsService metricsService;

    /// <summary>
    /// Creates the GA service.
    /// The metrics service is used to score candidate solutions.
    /// </summary>
    /// <param name="lsbService">LSB service kept for symmetry with the service graph. The optimizer itself scores candidates from cached LSB values.</param>
    /// <param name="metricsService">Metrics service used to convert MSE estimates into PSNR scores.</param>

    public GeneticAlgorithmService(LsbSteganographyService lsbService, MetricsService metricsService)
    {
        this.metricsService = metricsService;
    }

    /// <summary>
    /// Runs the full GA loop and returns the best chromosome found for the current payload and cover image.
    /// </summary>
    /// <param name="coverPixels">Original cover image pixels.</param>
    /// <param name="payloadBits">Encrypted payload bits that must be embedded.</param>
    /// <param name="candidateStartIndex">First logical RGB channel allowed for payload embedding after the reserved prefix.</param>
    /// <param name="rgbChannelCount">Total number of usable RGB channels in the cover image.</param>
    /// <param name="parameters">GA settings such as population size, generations, and mutation rate.</param>
    /// <param name="progressCallback">Optional callback that reports generation progress.</param>
    /// <returns>The best chromosome found by the optimizer.</returns>

    public Chromosome Optimize(
        byte[] coverPixels,
        IReadOnlyList<int> payloadBits,
        int candidateStartIndex,
        int rgbChannelCount,
        GaParameters parameters,
        Action<int, int, TimeSpan, TimeSpan>? progressCallback = null)
    {
        ArgumentNullException.ThrowIfNull(coverPixels);
        ArgumentNullException.ThrowIfNull(payloadBits);
        ArgumentNullException.ThrowIfNull(parameters);

        parameters.Validate();

        var geneCount = payloadBits.Count;
        if (geneCount <= 0)
        {
            throw new InvalidOperationException("Payload bit count must be positive.");
        }

        var candidateCount = rgbChannelCount - candidateStartIndex;
        if (candidateCount < geneCount)
        {
            throw new InvalidOperationException("Candidate channel count must be at least the payload bit count.");
        }

        var coverLsbBits = BuildCoverLsbMap(coverPixels, rgbChannelCount);
        var stopwatch = Stopwatch.StartNew();

        var population = CreateInitialPopulation(candidateStartIndex, rgbChannelCount, geneCount, parameters.PopulationSize, coverLsbBits, payloadBits);
        EvaluatePopulation(population, coverLsbBits, payloadBits, rgbChannelCount);
        population.Sort(CompareDescending);

        var best = population[0].Clone();
        var stagnantGenerations = 0;

        for (var generation = 0; generation < parameters.Generations; generation++)
        {
            population.Sort(CompareDescending);
            if (IsBetter(population[0], best))
            {
                best = population[0].Clone();
                stagnantGenerations = 0;
            }
            else
            {
                stagnantGenerations++;
            }

            var eliteCount = Math.Max(1, parameters.PopulationSize / 4);
            var next = new List<Chromosome>(parameters.PopulationSize);
            for (var i = 0; i < eliteCount && i < population.Count; i++)
            {
                next.Add(population[i].Clone());
            }

            var parents = SelectParentsBySus(population, Math.Max(2, parameters.PopulationSize));
            while (next.Count < parameters.PopulationSize)
            {
                var parentA = parents[Random.Shared.Next(parents.Count)];
                var parentB = parents[Random.Shared.Next(parents.Count)];
                var (child1Genes, child2Genes) = PmxSubset(parentA.Genes, parentB.Genes, candidateStartIndex, rgbChannelCount, parameters.MutationRate);

                next.Add(new Chromosome(child1Genes));
                if (next.Count < parameters.PopulationSize)
                {
                    next.Add(new Chromosome(child2Genes));
                }
            }

            EvaluatePopulation(next, coverLsbBits, payloadBits, rgbChannelCount);
            population = next;

            var completed = generation + 1;
            var elapsed = stopwatch.Elapsed;
            var remaining = completed == 0
                ? TimeSpan.Zero
                : TimeSpan.FromTicks((long)(elapsed.Ticks * ((double)(parameters.Generations - completed) / completed)));
            progressCallback?.Invoke(completed, parameters.Generations, elapsed, remaining);

            if (stagnantGenerations >= MaxStagnantGenerations)
            {
                break;
            }
        }

        population.Sort(CompareDescending);
        if (IsBetter(population[0], best))
        {
            best = population[0].Clone();
        }

        return best;
    }

    /// <summary>
    /// Builds a fast lookup array containing the current LSB value of every usable RGB channel.
    /// This allows the GA to score candidates without rewriting the image buffer each time.
    /// </summary>

    private static byte[] BuildCoverLsbMap(byte[] coverPixels, int rgbChannelCount)
    {
        var map = new byte[rgbChannelCount];
        for (var i = 0; i < rgbChannelCount; i++)
        {
            map[i] = (byte)(coverPixels[ChannelIndexHelper.ToByteOffset(i)] & 1);
        }

        return map;
    }

    /// <summary>
    /// Builds the initial population.
    /// It starts with one greedy solution and fills the rest with random valid chromosomes.
    /// </summary>

    private static List<Chromosome> CreateInitialPopulation(
        int candidateStartIndex,
        int rgbChannelCount,
        int geneCount,
        int populationSize,
        byte[] coverLsbBits,
        IReadOnlyList<int> payloadBits)
    {
        var population = new List<Chromosome>(populationSize)
        {
            new(CreateGreedyGenes(candidateStartIndex, rgbChannelCount, payloadBits, coverLsbBits))
        };

        while (population.Count < populationSize)
        {
            population.Add(new Chromosome(CreateRandomGenes(candidateStartIndex, rgbChannelCount, geneCount)));
        }

        return population;
    }

    /// <summary>
    /// Creates a greedy starting chromosome that tries to reuse channels whose current LSB already matches the payload bit.
    /// </summary>

    private static int[] CreateGreedyGenes(int candidateStartIndex, int rgbChannelCount, IReadOnlyList<int> payloadBits, byte[] coverLsbBits)
    {
        var genes = new int[payloadBits.Count];
        var searchIndex = candidateStartIndex;

        for (var bitIndex = 0; bitIndex < payloadBits.Count; bitIndex++)
        {
            var desiredBit = payloadBits[bitIndex];
            var matchedIndex = -1;
            for (var candidate = searchIndex; candidate < rgbChannelCount; candidate++)
            {
                if (coverLsbBits[candidate] == desiredBit)
                {
                    matchedIndex = candidate;
                    break;
                }
            }

            if (matchedIndex >= 0)
            {
                genes[bitIndex] = matchedIndex;
                searchIndex = matchedIndex + 1;
            }
            else
            {
                genes[bitIndex] = searchIndex;
                searchIndex++;
            }
        }

        return genes;
    }

    /// <summary>
    /// Creates a random chromosome with unique sorted logical channel indices.
    /// </summary>

    private static int[] CreateRandomGenes(int candidateStartIndex, int rgbChannelCount, int geneCount)
    {
        var rangeLength = rgbChannelCount - candidateStartIndex;
        var selected = new HashSet<int>(geneCount);

        for (var j = rangeLength - geneCount; j < rangeLength; j++)
        {
            var t = Random.Shared.Next(j + 1);
            if (!selected.Add(t))
            {
                selected.Add(j);
            }
        }

        var genes = selected.Select(value => value + candidateStartIndex).ToArray();
        Array.Sort(genes);
        return genes;
    }

    /// <summary>
    /// Evaluates all chromosomes in the population in parallel.
    /// </summary>
    /// <param name="population">Population to score.</param>
    /// <param name="coverLsbBits">Cached current LSB values of the cover image.</param>
    /// <param name="payloadBits">Payload bits to compare against.</param>
    /// <param name="rgbChannelCount">Total number of usable RGB channels.</param>

    private void EvaluatePopulation(List<Chromosome> population, byte[] coverLsbBits, IReadOnlyList<int> payloadBits, int rgbChannelCount)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) };
        Parallel.ForEach(population, options, chromosome => Evaluate(chromosome, coverLsbBits, payloadBits, rgbChannelCount));
    }

    /// <summary>
    /// Scores one chromosome by counting how many channels would change and converting that estimate into PSNR.
    /// </summary>
    /// <param name="chromosome">Chromosome to score.</param>
    /// <param name="coverLsbBits">Cached current LSB values of the cover image.</param>
    /// <param name="payloadBits">Payload bits to compare against.</param>
    /// <param name="rgbChannelCount">Total number of usable RGB channels.</param>

    private void Evaluate(Chromosome chromosome, byte[] coverLsbBits, IReadOnlyList<int> payloadBits, int rgbChannelCount)
    {
        var changes = 0;
        for (var i = 0; i < payloadBits.Count; i++)
        {
            if (coverLsbBits[chromosome.Genes[i]] != payloadBits[i])
            {
                changes++;
            }
        }

        chromosome.ChangesCount = changes;
        var mse = rgbChannelCount == 0 ? 0d : (double)changes / rgbChannelCount;
        chromosome.Psnr = this.metricsService.ComputePsnrFromMse(mse);
    }

    /// <summary>
    /// Returns true when a candidate should replace the current best solution.
    /// PSNR is compared first, and changes count is used as a tie-breaker.
    /// </summary>

    private static bool IsBetter(Chromosome candidate, Chromosome currentBest)
    {
        var difference = Math.Abs(candidate.Psnr - currentBest.Psnr);
        if (difference <= Epsilon)
        {
            return candidate.ChangesCount < currentBest.ChangesCount;
        }

        return candidate.Psnr > currentBest.Psnr;
    }

    /// <summary>
    /// Sorts chromosomes from best to worst according to the same scoring rule used by the optimizer.
    /// </summary>

    private static int CompareDescending(Chromosome a, Chromosome b)
    {
        var difference = Math.Abs(a.Psnr - b.Psnr);
        if (difference <= Epsilon)
        {
            return a.ChangesCount.CompareTo(b.ChangesCount);
        }

        return b.Psnr.CompareTo(a.Psnr);
    }

    /// <summary>
    /// Selects parents using stochastic universal sampling (SUS).
    /// This keeps pressure toward good solutions while preserving population diversity.
    /// </summary>

    private static List<Chromosome> SelectParentsBySus(List<Chromosome> population, int count)
    {
        var minPsnr = population.Min(c => c.Psnr);
        var weights = population
            .Select(c => Math.Max(1e-9, (c.Psnr - minPsnr) + 1e-6 + (1.0 / (1_000_000.0 + c.ChangesCount))))
            .ToArray();

        var totalWeight = weights.Sum();
        var step = totalWeight / count;
        var start = step / 2.0;

        var selected = new List<Chromosome>(count);
        var cumulative = 0.0;
        var index = 0;

        for (var pointIndex = 0; pointIndex < count; pointIndex++)
        {
            var point = start + (pointIndex * step);
            while (index < population.Count && cumulative + weights[index] < point)
            {
                cumulative += weights[index];
                index++;
            }

            if (index >= population.Count)
            {
                index = population.Count - 1;
            }

            selected.Add(population[index]);
        }

        return selected;
    }

    /// <summary>
    /// Builds two children from two parents using the project's custom crossover strategy for sorted unique channel positions.
    /// </summary>
    private static (int[] Child1, int[] Child2) PmxSubset(int[] parent1, int[] parent2, int candidateStartIndex, int rgbChannelCount, double mutationRate)
    {
        var child1 = BuildChild(parent1, parent2, candidateStartIndex, rgbChannelCount, mutationRate);
        var child2 = BuildChild(parent2, parent1, candidateStartIndex, rgbChannelCount, mutationRate);
        return (child1, child2);
    }

    /// <summary>
    /// Builds one child by mixing genes from both parents, filling gaps when needed, and optionally mutating the result.
    /// </summary>

    private static int[] BuildChild(int[] primaryParent, int[] secondaryParent, int candidateStartIndex, int rgbChannelCount, double mutationRate)
    {
        var length = primaryParent.Length;
        var child = new int[length];
        var used = new HashSet<int>(length);
        var primaryIndex = 0;
        var secondaryIndex = 0;
        var candidate = candidateStartIndex;

        for (var i = 0; i < length; i++)
        {
            int gene;
            if ((i & 1) == 0)
            {
                gene = NextUnused(primaryParent, ref primaryIndex, used);
            }
            else
            {
                gene = NextUnused(secondaryParent, ref secondaryIndex, used);
            }

            if (gene == -1)
            {
                gene = NextUnused(primaryParent, ref primaryIndex, used);
            }

            if (gene == -1)
            {
                gene = NextUnused(secondaryParent, ref secondaryIndex, used);
            }

            if (gene == -1)
            {
                while (candidate < rgbChannelCount && used.Contains(candidate))
                {
                    candidate++;
                }

                if (candidate >= rgbChannelCount)
                {
                    throw new InvalidOperationException("Unable to fill child chromosome with unique genes.");
                }

                gene = candidate;
                candidate++;
            }

            child[i] = gene;
            used.Add(gene);
        }

        if (ShouldMutate(mutationRate))
        {
            MutateByReplacement(child, candidateStartIndex, rgbChannelCount);
        }

        Array.Sort(child);
        return child;
    }

    /// <summary>
    /// Returns the next gene from a parent that has not yet been used in the child chromosome.
    /// </summary>

    private static int NextUnused(int[] source, ref int index, HashSet<int> used)
    {
        while (index < source.Length)
        {
            var gene = source[index++];
            if (!used.Contains(gene))
            {
                return gene;
            }
        }

        return -1;
    }

    /// <summary>
    /// Decides whether mutation should happen for the current child based on the configured mutation rate.
    /// </summary>

    private static bool ShouldMutate(double mutationRate)
    {
        if (mutationRate <= 0)
        {
            return false;
        }

        if (mutationRate >= 1)
        {
            return true;
        }

        return Random.Shared.NextDouble() < mutationRate;
    }

    /// <summary>
    /// Mutates a chromosome by replacing one gene with another valid unused channel index.
    /// </summary>

    private static void MutateByReplacement(int[] genes, int candidateStartIndex, int rgbChannelCount)
    {
        if (genes.Length == 0)
        {
            return;
        }

        var replaceIndex = Random.Shared.Next(genes.Length);
        var used = new HashSet<int>(genes);
        used.Remove(genes[replaceIndex]);

        var attempts = 0;
        var candidate = genes[replaceIndex];
        while (attempts < 32)
        {
            candidate = Random.Shared.Next(candidateStartIndex, rgbChannelCount);
            if (!used.Contains(candidate))
            {
                genes[replaceIndex] = candidate;
                return;
            }

            attempts++;
        }

        candidate = candidateStartIndex;
        while (candidate < rgbChannelCount)
        {
            if (!used.Contains(candidate))
            {
                genes[replaceIndex] = candidate;
                return;
            }

            candidate++;
        }
    }
}
