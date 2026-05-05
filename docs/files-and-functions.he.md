# פירוט קבצים ופונקציות (מדריך למפתח חדש)

המסמך הזה מסביר **מה כל פונקציה עושה**, ואיך הזרימה עובדת בפועל בקוד.

---

## App

### `App.xaml`
- מגדיר את אפליקציית WPF ואת חלון הפתיחה.

### `App.xaml.cs`
- `OnStartup(...)`:
  - נקודת כניסה של האפליקציה.
  - רושם מטפלי חריגות גלובליים (UI thread / AppDomain) כדי להציג שגיאות למשתמש במקום קריסה "שקטה".

---

## Models

### `Models/CoreModels.cs`

#### `Chromosome`
- `Chromosome(int[] genes)`:
  - יוצר כרומוזום עבור האלגוריתם הגנטי (GA).
  - `genes` הם אינדקסים לוגיים של ערוצי RGB שבהם יוטמעו ביטי ה-payload.
- `Clone()`:
  - מבצע העתקה עמוקה של מערך הגנים ומעתיק גם PSNR ומספר שינויים.

#### `GaParameters`
- `Validate()`:
  - בודק ש־`PopulationSize >= 2`, `Generations >= 1`, וש־`MutationRate` בטווח `[0,1]`.

#### `HeaderInfo`
- DTO בלבד (ללא פונקציות): שומר רוחב/גובה payload, אורכי payload/metadata ו־CRC32.

#### `PngImageData`
- `PngImageData(int width, int height, byte[] pixels)`:
  - עוטף תמונת PNG בזיכרון כ־BGRA32.
  - מבצע ולידציה שאורך הבאפר הוא `width * height * 4`.

### `Models/OperationModels.cs`
- `EmbedResult`, `ExtractionResult`, `OperationEstimate`, `OperationProgress`:
  - מחלקות תוצאה/התקדמות בלבד, ללא לוגיקה חישובית.

---

## Services – תשתיות ביטים/הצפנה/Checksum

### `Services/StegoSupportServices.cs`

#### `BinaryHeaderService`
- `BuildHeader(...)`:
  - בונה Header בינארי קבוע של 24 בתים (6 שדות של 4 בתים).
  - שומר מידות payload, אורכים ו־CRCים.
- `ParseHeader(byte[] header)`:
  - מפענח Header בינארי לאובייקט `HeaderInfo`.
  - בודק גודל Header ותקינות ערכים חיוביים.
- `CopyIntToBuffer(...)`, `CopyUIntToBuffer(...)`:
  - עזר פרטי לכתיבת מספרים לבאפר בתים.
- `ReadIntFromBuffer(...)`, `ReadUIntFromBuffer(...)`:
  - עזר פרטי לקריאת מספרים מבאפר בתים.

#### `BitStreamService`
- `ToBits(byte[] bytes)`:
  - ממיר בתים לרצף ביטים (MSB-first), כדי להטמיע ביט־ביט.
- `ToBytes(IReadOnlyList<int> bits)`:
  - ממיר בחזרה מביטים לבתים.
  - דורש מספר ביטים מתחלק ב־8 וערכים 0/1 בלבד.

#### `ChannelIndexHelper`
- `ToByteOffset(int logicalRgbChannelIndex)`:
  - ממפה אינדקס ערוץ RGB "לוגי" ל־offset אמיתי בבאפר BGRA.
  - מדלג על Alpha.

#### `Crc32Service`
- `Compute(byte[] data)`:
  - מחשב CRC32 לאימות שלמות metadata/payload.
- `BuildTable()`:
  - בונה טבלת Lookup של CRC32 (פעם אחת סטטית).

#### `XorCipherService`
- `Apply(byte[] data, string? key)`:
  - מבצע XOR סימטרי (אותה פונקציה להצפנה ולפענוח).
  - אם אין מפתח – מחזיר עותק הנתונים ללא שינוי.
- `BuildDerivedKeyBytes(string key)`:
  - גוזר רצף בתים מהמפתח בעזרת SHA256 כדי לקבל keystream יציב.

---

## Services – סטגנוגרפיה ותמונות

### `Services/ImageStegoServices.cs`

#### `LsbSteganographyService`
- `WriteBit(byte[] pixels, int logicalRgbChannelIndex, int bit)`:
  - כותב ביט ב-LSB של ערוץ RGB נבחר.
  - שומר על תחום 0..255 וממזער שינוי (±1 כשצריך).
  - מחזיר האם באמת בוצע שינוי.
- `ReadBit(byte[] pixels, int logicalRgbChannelIndex)`:
  - קורא את ביט ה-LSB מערוץ RGB נבחר.

#### `MetricsService`
- `ComputeMseRgb(byte[] originalPixels, byte[] modifiedPixels)`:
  - מחשב MSE על RGB בלבד (ללא Alpha).
- `ComputePsnrRgb(...)`:
  - מחשב PSNR מתוך שתי תמונות (דרך MSE).
- `ComputePsnrFromMse(double mse)`:
  - ממיר MSE ל־PSNR לפי נוסחת 8-bit סטנדרטית.

### `Services/ImageIOService.cs`
- `LoadPng(string path)`:
  - טוען PNG מהדיסק וממיר ל־`PngImageData` בפורמט BGRA32.
- `SavePng(PngImageData image, string path)`:
  - שומר `PngImageData` כ־PNG, כולל יצירת תיקייה אם צריך.

---

## Services – האלגוריתם הגנטי

### `Services/GeneticAlgorithmService.cs`

- `GeneticAlgorithmService(...)`:
  - מקבל תלויות; בפועל משתמש ב־`MetricsService` לדירוג.
- `Optimize(...)`:
  - פונקציית הליבה של GA.
  - שלבים:
    1. ולידציות לקלט.
    2. בניית מפת LSB מקורית של cover.
    3. יצירת אוכלוסייה התחלתית.
    4. הערכה, מיון, אליטיזם.
    5. בחירת הורים (SUS), crossover, mutation.
    6. עצירה מוקדמת בקיפאון.
    7. החזרת הפתרון הטוב ביותר.
- `BuildCoverLsbMap(...)`:
  - מייצר מערך LSB של כל ערוצי RGB, כדי לחשב fitness מהר.
- `CreateInitialPopulation(...)`:
  - מתחיל בכרומוזום גרידי + כרומוזומים אקראיים.
- `CreateGreedyGenes(...)`:
  - בוחר אינדקסים שממקסמים התאמה מוקדמת לביטי payload.
- `CreateRandomGenes(...)`:
  - יוצר כרומוזום אקראי חוקי (אינדקסים ייחודיים וממוינים).
- `EvaluatePopulation(...)`:
  - מחשב fitness במקביל לכל כרומוזום.
- `Evaluate(...)`:
  - סופר כמה ערוצים ישתנו וממיר להערכת PSNR.
- `IsBetter(...)`:
  - משווה שני כרומוזומים (PSNR ואז tie-break לפי שינויים).
- `CompareDescending(...)`:
  - comparator למיון מהטוב לגרוע.
- `SelectParentsBySus(...)`:
  - בחירת הורים בשיטת Stochastic Universal Sampling.
- `PmxSubset(...)`:
  - מייצר שני ילדים מהורים (וריאציית crossover על סאבסט).
- `BuildChild(...)`:
  - בונה ילד יחיד תוך שמירה על חוקיות סדר/טווח/ייחודיות.
- `NextUnused(...)`:
  - מוצא גן פנוי הבא שלא בשימוש.
- `ShouldMutate(...)`:
  - החלטה אם לבצע mutation לפי ההסתברות.
- `MutateByReplacement(...)`:
  - מחליף גן בגן חוקי אחר ושומר מיון.

---

## Services – האורקסטרציה הראשית

### `Services/SteganographyCoordinator.cs`

#### פעולות ציבוריות (API ראשי)
- `SteganographyCoordinator(...)`:
  - מקבל את כל השירותים (DI).
- `Embed(...)`:
  - הפייפליין המלא להטמעה:
    1. טעינת cover/payload ובדיקת PNG signature.
    2. XOR payload והמרה לביטים.
    3. חישוב מקום ל-header+metadata ובדיקת קיבולת.
    4. הרצת GA לבחירת מיקומי הטמעה.
    5. סריאליזציה ודחיסה של metadata (genes), CRC.
    6. בניית header, XOR ל-header/metadata.
    7. כתיבת header+metadata בצורה רציפה בתחילת הערוצים.
    8. הטמעת payload במיקומי genes.
    9. שמירת PNG stego והחזרת מדדים.
- `Extract(...)`:
  - הפייפליין המלא לחילוץ:
    1. טעינת stego.
    2. קריאה ופענוח header.
    3. קריאה ופענוח metadata.
    4. בדיקות CRC ל-metadata.
    5. שחזור genes וולידציה שלהם.
    6. קריאת ביטי payload מהמיקומים.
    7. XOR לפענוח payload, בדיקת CRC, בדיקת PNG signature.
    8. שמירת קובץ payload והחזרת תוצאה.
- `CalculateMaximumPayloadBytes(...)`:
  - מחשב בקירוב בינארי את גודל payload המקסימלי לתמונת cover.
- `CalculateMinimumCoverPixels(...)`:
  - מחשב כמה פיקסלים מינימליים נדרשים לגודל payload נתון.
- `EstimateEmbedOperation(...)`:
  - בונה הערכת זמן/קיבולת ל־UI לפי גודל תמונות ופרמטרי GA.

#### פונקציות עזר פרטיות
- `CalculateEstimatedTotalRequiredBits(...)`: מחשב סה"כ ביטים נדרשים (header+metadata+payload).
- `EstimateMetadataByteLength(...)`: אומדן גודל metadata דחוס.
- `ShiftGenes(...)`: מזיז את genes כשנדרש יותר מקום ב-prefix.
- `Report(...)`: אחיד לדיווח התקדמות (`OperationProgress`).
- `SerializeGenesCompressed(...)`: דוחס genes לבתים (Rice coding על דלתאות).
- `DeserializeGenesCompressed(...)`: מפענח metadata דחוס חזרה ל־genes.
- `ChooseRiceParameter(...)`: בוחר פרמטר k לקידוד Rice לפי ממוצע פערים.
- `PadMetadata(...)`: ממלא metadata לגודל שמור קבוע.
- `CalculateAverageDelta(...)`: מחשב ממוצע מרווחים בין genes.
- `ValidateHeader(...)`: מאמת התאמת header לקיבולת.
- `ValidateGenes(...)`: מאמת genes (ממוינים, בטווח, לא חופפים prefix).
- `ValidatePngSignature(...)`: מוודא שהמידע הוא PNG.
- `EnsureCapacity(...)`: מוודא שיש מספיק ערוצים ל-prefix+payload.
- `WriteSequentialBits(...)`: כותב רצף ביטים החל מאינדקס לוגי נתון.
- `ReadSequentialBits(...)`: קורא רצף ביטים החל מאינדקס לוגי נתון.
- `CountChangedChannels(...)`: סופר כמה ערוצים באמת השתנו בין cover ל-stego.

#### מחלקות פנימיות לאריזה ביטית
- `PackedBitWriter`
  - `PackedBitWriter(Stream stream)`: אתחול כותב ביטים לזרם.
  - `WriteRice(int value, int parameter)`: כתיבת מספר בקידוד Rice.
  - `Flush()`: כתיבת שאריות בית לבאפר.
  - `WriteBit(int bit)`: כתיבת ביט יחיד (עזר פרטי).
- `PackedBitReader`
  - `PackedBitReader(Stream stream)`: אתחול קורא ביטים מזרם.
  - `ReadRice(int parameter)`: קריאת מספר בקידוד Rice.
  - `ReadBit()`: קריאת ביט יחיד (עזר פרטי).

---

## ViewModels

### `ViewModels/MvvmInfrastructure.cs`

#### `BaseViewModel`
- `SetProperty<T>(...)`:
  - מעדכן שדה רק אם הערך באמת השתנה, ואז מרים `PropertyChanged`.
- `RaisePropertyChanged(...)`:
  - הרמת `PropertyChanged` ידנית.

#### `RelayCommand`
- `RelayCommand(Action execute, Func<bool>? canExecute = null)`:
  - מגדיר פעולה + תנאי זמינות.
- `CanExecute(...)`: האם הפקודה זמינה כרגע.
- `Execute(...)`: מריץ את הפעולה.
- `RaiseCanExecuteChanged()`: מעדכן את WPF לשינוי מצב Enable/Disable.

### `ViewModels/MainViewModel.cs`

- `MainViewModel(...)`:
  - קונסטרקטור שמחבר שירותים, יוצר פקודות, ומבצע רענון ראשוני.
- `EmbedAsync()`:
  - אוסף קלט מה-UI, בונה פרמטרים, מפעיל `Embed`, ומעדכן טקסט/תצוגה מקדימה/התקדמות.
- `ExtractAsync()`:
  - אוסף קלט מה-UI, מפעיל `Extract`, ומעדכן סטטוס/תצוגה מקדימה.
- `OnOperationProgress(...)`:
  - ממפה אירועי התקדמות מהשירות למאפייני UI.
- `SetEmbedMode()` / `SetExtractMode()`:
  - מחליף מצב מסך בין הטמעה לחילוץ ומאתחל טקסטים/שדות רלוונטיים.
- `BuildValidatedParameters()`:
  - בונה `GaParameters` מה־UI ומאמת אותם.
- `BuildValidatedParametersSafe()`:
  - גרסה בטוחה לבניית פרמטרים (לצרכי חישובי תצוגה/אומדן בלי להפיל את ה־UI).
- `BrowsePayload()` / `BrowseCover()` / `BrowseStegoInput()` / `BrowseOutputFolder()`:
  - פתיחת דיאלוגים לבחירת קבצים/תיקייה ועדכון שדות.
- `RefreshPathDependentState()`:
  - מרענן תצוגות ונתונים שתלויים בנתיבי קבצים.
- `RefreshComputedTexts()`:
  - מעדכן טקסטי קיבולת/אומדן זמן/אזהרות לפי הקלט הנוכחי.
- `GetDefaultOutputFolderPath()`:
  - קובע תיקיית פלט ברירת מחדל.
- `NormalizePngInputPath(string path)`:
  - מנרמל ערכי נתיב מה-UI.
- `TryGetImageData(...)`:
  - טוען מידע תמונה בצורה בטוחה עם `out` (כולל גודל קובץ).
- `BuildOutputPath()`:
  - בונה נתיב פלט סופי לפי תיקייה ושם קובץ.
- `LoadPreview(string path)`:
  - טוען תצוגה מקדימה (`ImageSource`) לקובץ PNG.
- `FormatBytes(long value)`:
  - מציג גדלים בפורמט ידידותי (B/KB/MB...).
- `FormatDuration(TimeSpan value)`:
  - מציג זמן בפורמט קריא.
- `CreateOpenPngDialog()`:
  - מחזיר OpenFileDialog מוגדר לסינון PNG.
- `RefreshCommands()`:
  - מרענן מצב Enable של הפקודות לפי `IsBusy`/מצב מסך.

> הערה: יש הרבה Properties ב־`MainViewModel` שמטרתם להחזיק state ל־UI. ה"לוגיקה" המרכזית נמצאת בפונקציות שלמעלה.

---

## Views

### `Views/MainWindow.xaml`
- מסך UI: אזורי הטמעה/חילוץ, קלט נתיבים, פרמטרי GA, אומדנים, התקדמות ותוצאה.

### `Views/MainWindow.xaml.cs`
- `MainWindow()`:
  - בונה את גרף התלויות ידנית (services + view model), ומקשר את ה־DataContext.

---

## איך לקרוא את הקוד בפועל (סדר מומלץ למפתח חדש)
1. להתחיל מ־`MainViewModel` כדי להבין את זרימות המשתמש.
2. לעבור ל־`SteganographyCoordinator` להבין את orchestration.
3. להעמיק ב־`GeneticAlgorithmService` להבין בחירת מיקומים.
4. לקרוא `StegoSupportServices` ו־`ImageStegoServices` כדי להבין ביטים/LSB/CRC/XOR.
5. לסיים ב־Models ו־View כדי להשלים תמונה.
