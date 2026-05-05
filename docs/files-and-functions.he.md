# פירוט קבצים ופונקציות

## קבצי תשתית אפליקציה
- **`App.xaml`**: הגדרת אפליקציית WPF ונתיב חלון הפתיחה.
- **`App.xaml.cs`**:
  - `OnStartup(...)`: רישום מטפלי חריגות גלובליים (UI thread + AppDomain) והצגת הודעות שגיאה.

## Models
- **`Models/CoreModels.cs`**:
  - `Chromosome`: מייצג פתרון GA (גנים, PSNR, מספר שינויים).
    - `Clone()`: שכפול עמוק של כרומוזום.
  - `GaParameters`: פרמטרי GA.
    - `Validate()`: אימות טווחים של גודל אוכלוסייה/דורות/מוטציה.
  - `HeaderInfo`: שדות הכותרת הבינארית לחילוץ.
  - `PngImageData`: מעטפת תמונה בזיכרון BGRA32 עם אימות אורך באפר.
- **`Models/OperationModels.cs`**:
  - `EmbedResult`: תוצאת הטמעה.
  - `ExtractionResult`: תוצאת חילוץ.
  - `OperationEstimate`: הערכת קיבולת/זמן.
  - `OperationProgress`: מבנה דיווח התקדמות.

## Services
- **`Services/StegoSupportServices.cs`**:
  - `BinaryHeaderService`:
    - `BuildHeader(...)`: בניית Header בגודל 24 בתים.
    - `ParseHeader(...)`: פירוש Header חזרה לאובייקט `HeaderInfo`.
    - פונקציות פרטיות: `CopyIntToBuffer`, `CopyUIntToBuffer`, `ReadIntFromBuffer`, `ReadUIntFromBuffer`.
  - `BitStreamService`:
    - `ToBits(...)`: המרת בתים לביטים (MSB-first).
    - `ToBytes(...)`: המרת ביטים לבתים.
  - `ChannelIndexHelper`:
    - `ToByteOffset(...)`: מיפוי אינדקס RGB ל-offset ב-BGRA תוך דילוג על Alpha.
  - `Crc32Service`:
    - `Compute(...)`: חישוב CRC32.
    - `BuildTable()`: בניית טבלת lookup של CRC32.
  - `XorCipherService`:
    - `Apply(...)`: הצפנה/פענוח סימטריים ב-XOR.
    - `BuildDerivedKeyBytes(...)`: גזירת רצף בתים מהמפתח הטקסטואלי.
- **`Services/GeneticAlgorithmService.cs`**:
  - מנהל את כל לולאת GA: יצירת אוכלוסייה, הערכה, בחירה, crossover, mutation, עצירה מוקדמת והחזרת הכרומוזום הטוב ביותר.
- **`Services/ImageIOService.cs`**:
  - קריאה/כתיבה של PNG, המרות ל-`PngImageData`, ושגרות עזר לנתיבי קבצים/תמונות.
- **`Services/ImageStegoServices.cs`**:
  - `LsbSteganographyService`: פעולות הטמעה/קריאה של ביטים בערוצי RGB ברמת LSB.
  - `MetricsService`: חישובי MSE ו-PSNR להערכת איכות תמונת Stego.
- **`Services/SteganographyCoordinator.cs`**:
  - `Embed(...)`: orchestrator של כל מהלך ההטמעה.
  - `Extract(...)`: orchestrator של כל מהלך החילוץ.
  - כולל גם פונקציות עזר פנימיות לאמידת זמן/קיבולת, סידור metadata, ודיווח התקדמות.

## ViewModels
- **`ViewModels/MvvmInfrastructure.cs`**:
  - `ViewModelBase`: מימוש `INotifyPropertyChanged` ו-`SetProperty`.
  - `RelayCommand`: מימוש ICommand עם `CanExecute`/`Execute` ו-`RaiseCanExecuteChanged`.
- **`ViewModels/MainViewModel.cs`**:
  - מאפייני state לקבצים, פרמטרים, תצוגות מקדימות, סטטוס והתקדמות.
  - פקודות UI: מעבר מצב Embed/Extract, בחירת קבצים/תיקייה, הרצת Embed/Extract.
  - `EmbedAsync()`: מפעיל הטמעה, מעדכן התקדמות, ומציג מדדים/תוצאה.
  - `ExtractAsync()`: מפעיל חילוץ, מציג קובץ שחולץ ופרטי תוצאה.
  - פונקציות עזר פנימיות לאימות נתיבים, טעינת Preview, בניית פרמטרים וחישובי הערכה.

## Views
- **`Views/MainWindow.xaml`**:
  - פריסת המסך הראשי: מצב הטמעה, מצב חילוץ, פרמטרים, הערכות, פס התקדמות ולוג תוצאה.
- **`Views/MainWindow.xaml.cs`**:
  - בניית dependency graph ידנית וקישור ה-ViewModel לחלון.
