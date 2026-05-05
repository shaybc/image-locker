# מדריך שימוש למשתמש

## מטרת האפליקציה
האפליקציה מאפשרת להסתיר תמונת PNG אחת (Payload) בתוך תמונת PNG אחרת (Cover), ולחלץ אותה בהמשך.

## דרישות בסיס
- קבצי PNG תקינים.
- מומלץ לבחור תמונת Cover גדולה משמעותית מתמונת ה-Payload כדי לשמור על איכות.

## מצב Embed (הטמעה)
1. לחץ על **Embed**.
2. בחר **Select Hidden Image** (תמונת ה-Payload).
3. בחר **Select Cover Image** (תמונת ה-Cover).
4. הזן מפתח הצפנה בשדה **Encryption key** (אופציונלי אך מומלץ).
5. כוון פרמטרי GA:
   - Population size
   - Generations
   - Mutation rate (0-1)
6. בחר תיקיית יעד בשדה **Output folder**.
7. (אופציונלי) שנה את שם קובץ הפלט.
8. לחץ **Embed The Image**.
9. עקוב אחרי Progress ו-Status עד הודעת הצלחה.

## מצב Extract (חילוץ)
1. לחץ על **Extract**.
2. בחר **Select Stego Image**.
3. הזן את אותו **Encryption key** ששימש בהטמעה.
4. בחר תיקיית יעד.
5. לחץ **Extract The Image**.
6. בדוק את תצוגת התוצאה ואת נתוני החילוץ ב-Status.

## פירוש שדות תוצאה
- **Header bits / Metadata bits / Payload bits**: כמה ביטים נשמרו לכל רכיב.
- **Changed channels**: כמה ערוצי RGB השתנו בפועל.
- **MSE/PSNR**: מדדי איכות; בדרך כלל PSNR גבוה יותר מעיד על פחות פגיעה נראית.

## תקלות נפוצות
- **אין קיבולת מספקת**: בחר Cover גדול יותר או Payload קטן יותר.
- **כשל CRC או פענוח**: ודא שימוש במפתח XOR הנכון ושקובץ ה-Stego לא שונה.
- **שגיאת פורמט**: ודא שמדובר ב-PNG תקין.
