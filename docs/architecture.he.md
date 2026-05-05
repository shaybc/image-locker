# ארכיטקטורה

## מבט על
האפליקציה בנויה כיישום WPF עם הפרדת אחריות בסגנון MVVM:
- **View**: שכבת הממשק (XAML).
- **ViewModel**: לוגיקת UI, פקודות, סטייט ותזמון פעולות.
- **Services**: מנוע סטגנוגרפיה, הצפנה, בדיקות תקינות, מדדים ואלגוריתם גנטי.
- **Models**: אובייקטים להעברת נתונים בין שכבות.

## שכבות ורכיבים

### 1) שכבת UI
- `Views/MainWindow.xaml` מציג שני מצבים: הטמעה (Embed) וחילוץ (Extract), פרמטרים של GA, פס התקדמות, ופאנל סטטוס.
- `Views/MainWindow.xaml.cs` מבצע קומפוזיציה ידנית של כל השירותים ומגדיר `DataContext` ל-`MainViewModel`.

### 2) שכבת ViewModel
- `ViewModels/MainViewModel.cs` מנהל את כל זרימת העבודה מהמסך:
  - בחירת קבצים
  - אימות שדות
  - קריאה ל-`SteganographyCoordinator`
  - הצגת תוצאות והתקדמות
- `ViewModels/MvvmInfrastructure.cs` מספק תשתית `INotifyPropertyChanged` ו-`RelayCommand`.

### 3) שכבת Services
- `SteganographyCoordinator` הוא Orchestrator שמנהל Embed/Extract מקצה לקצה.
- `ImageIOService` קורא/כותב תמונות PNG ומבצע המרות ל-BGRA32.
- `BinaryHeaderService` בונה/מפרש Header בינארי קבוע בגודל 24 בתים.
- `BitStreamService` ממיר בין בתים לביטים ולהפך.
- `XorCipherService` מבצע XOR סימטרי להצפנה/פענוח.
- `LsbSteganographyService` מטמיע/קורא ביטים בערוצי RGB בשיטת LSB.
- `MetricsService` מחשב MSE/PSNR.
- `GeneticAlgorithmService` בוחר מיקומי הטמעה אופטימליים להפחתת שינוי חזותי.
- `Crc32Service` מחשב CRC32 לאימות תקינות metadata ו-payload.

### 4) שכבת Models
מחלקות DTO ותצורה כמו `PngImageData`, `GaParameters`, `EmbedResult`, `ExtractionResult`, `OperationProgress` ועוד.

## זרימת נתונים גבוהה (Embed)
1. טעינת תמונת Cover + תמונת Payload.
2. המרת Payload ל-PNG bytes.
3. הצפנת payload ב-XOR (אם קיים מפתח).
4. חישוב ביטים: Header + Metadata + Payload.
5. הרצת GA לבחירת ערוצי RGB מתאימים.
6. הטמעת הביטים בערוצי RGB (ללא Alpha).
7. שמירת Stego PNG והחזרת מדדי איכות.

## זרימת נתונים גבוהה (Extract)
1. טעינת Stego PNG.
2. קריאת Header קבוע מה-prefix של הערוצים.
3. קריאת Metadata ופענוח מפת ההטמעה.
4. קריאת Payload bits לפי המיקומים השמורים.
5. בדיקת CRC32, פענוח XOR, ושחזור PNG.
6. שמירת התמונה המחולצת.

## עקרונות תכן בולטים
- הפרדה ברורה בין UI לבין לוגיקת דומיין.
- Coordinator יחיד שמרכז orchestration ומקטין תלות של ה-ViewModel בפרטים פנימיים.
- אימות נתונים ו-CRC כדי להפחית שגיאות שקטות.
- שימוש ב-GA כדי לשפר PSNR ולצמצם ארטיפקטים נראים לעין.
