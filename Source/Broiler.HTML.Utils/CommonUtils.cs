using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Drawing;

namespace Broiler.HTML.Utils;

internal delegate void ActionInt<in T>(T obj);
internal delegate void ActionInt<in T1, in T2>(T1 arg1, T2 arg2);
internal delegate void ActionInt<in T1, in T2, in T3>(T1 arg1, T2 arg2, T3 arg3);

internal static class CommonUtils
{
    private static readonly string[,] _romanDigitsTable =
    {
        { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" },
        { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" },
        { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" },
        {
            "", "M", "MM", "MMM", "M(V)", "(V)", "(V)M",
            "(V)MM", "(V)MMM", "M(X)"
        }
    };

    private static readonly string[,] _hebrewDigitsTable =
    {
        { "א", "ב", "ג", "ד", "ה", "ו", "ז", "ח", "ט" },
        { "י", "כ", "ל", "מ", "נ", "ס", "ע", "פ", "צ" },
        { "ק", "ר", "ש", "ת", "תק", "תר", "תש", "תת", "תתק", }
    };

    private static readonly string[,] _georgianDigitsTable =
    {
        { "ა", "ბ", "გ", "დ", "ე", "ვ", "ზ", "ჱ", "თ" },
        { "ი", "პ", "ლ", "მ", "ნ", "ჲ", "ო", "პ", "ჟ" },
        { "რ", "ს", "ტ", "ჳ", "ფ", "ქ", "ღ", "ყ", "შ" }
    };

    private static readonly string[,] _armenianDigitsTable =
    {
        { "Ա", "Բ", "Գ", "Դ", "Ե", "Զ", "Է", "Ը", "Թ" },
        { "Ժ", "Ի", "Լ", "Խ", "Ծ", "Կ", "Հ", "Ձ", "Ղ" },
        { "Ճ", "Մ", "Յ", "Ն", "Շ", "Ո", "Չ", "Պ", "Ջ" }
    };

    private static readonly string[] _hiraganaDigitsTable =
    [
        "あ", "ぃ", "ぅ", "ぇ", "ぉ", "か", "き", "く", "け", "こ", "さ", "し", "す", "せ", "そ", "た", "ち", "つ", "て", "と", "な", "に", "ぬ", "ね", "の", "は", "ひ", "ふ", "へ", "ほ", "ま", "み", "む", "め", "も", "ゃ", "ゅ", "ょ", "ら", "り", "る", "れ", "ろ", "ゎ", "ゐ", "ゑ", "を", "ん"
    ];

    private static readonly string[] _satakanaDigitsTable =
    [
        "ア", "イ", "ウ", "エ", "オ", "カ", "キ", "ク", "ケ", "コ", "サ", "シ", "ス", "セ", "ソ", "タ", "チ", "ツ", "テ", "ト", "ナ", "ニ", "ヌ", "ネ", "ノ", "ハ", "ヒ", "フ", "ヘ", "ホ", "マ", "ミ", "ム", "メ", "モ", "ヤ", "ユ", "ヨ", "ラ", "リ", "ル", "レ", "ロ", "ワ", "ヰ", "ヱ", "ヲ", "ン"
    ];

    public static string _tempPath;

    public static bool IsAsianCharecter(char ch) => ch >= 0x4e00 && ch <= 0xFA2D;

    public static bool IsDigit(char ch, bool hex = false) => (ch >= '0' && ch <= '9') || (hex && ((ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')));

    public static int ToDigit(char ch, bool hex = false)
    {
        if (ch >= '0' && ch <= '9')
            return ch - '0';
        else if (hex)
        {
            if (ch >= 'a' && ch <= 'f')
                return ch - 'a' + 10;
            else if (ch >= 'A' && ch <= 'F')
                return ch - 'A' + 10;
        }

        return 0;
    }

    public static SizeF Max(SizeF size, SizeF other) => new(Math.Max(size.Width, other.Width), Math.Max(size.Height, other.Height));

    public static Uri TryGetUri(string path)
    {
        try
        {
            if (Uri.IsWellFormedUriString(path, UriKind.RelativeOrAbsolute))
            {
                return new Uri(path, UriKind.RelativeOrAbsolute);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HtmlRenderer] CommonUtils.TryGetUri failed for path '{path}': {ex.Message}");
        }

        return null;
    }

    public static TValue GetFirstValueOrDefault<TKey, TValue>(IDictionary<TKey, TValue> dic, TValue defaultValue = default(TValue))
    {
        if (dic != null)
        {
            foreach (var value in dic)
                return value.Value;
        }

        return defaultValue;
    }

    public static FileInfo TryGetFileInfo(string path)
    {
        try
        {
            return new FileInfo(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HtmlRenderer] CommonUtils.TryGetFileInfo failed for path '{path}': {ex.Message}");
        }

        return null;
    }

    public static FileInfo GetLocalfileName(Uri imageUri)
    {
        StringBuilder fileNameBuilder = new();
        string absoluteUri = imageUri.AbsoluteUri;
        int lastSlash = absoluteUri.LastIndexOf('/');

        if (lastSlash == -1)
            return null;

        string uriUntilSlash = absoluteUri.Substring(0, lastSlash);
        fileNameBuilder.Append(uriUntilSlash.GetHashCode().ToString());
        fileNameBuilder.Append('_');

        string restOfUri = absoluteUri.Substring(lastSlash + 1);
        int indexOfParams = restOfUri.IndexOf('?');

        if (indexOfParams == -1)
        {
            string ext = ".png";
            int indexOfDot = restOfUri.IndexOf('.');
            if (indexOfDot > -1)
            {
                ext = restOfUri.Substring(indexOfDot);
                restOfUri = restOfUri.Substring(0, indexOfDot);
            }

            fileNameBuilder.Append(restOfUri);
            fileNameBuilder.Append(ext);
        }
        else
        {
            int indexOfDot = restOfUri.IndexOf('.');
            if (indexOfDot == -1 || indexOfDot > indexOfParams)
            {
                //The uri is not for a filename
                fileNameBuilder.Append(restOfUri);
                fileNameBuilder.Append(".png");
            }
            else if (indexOfParams > indexOfDot)
            {
                //Adds the filename without extension.
                fileNameBuilder.Append(restOfUri, 0, indexOfDot);
                //Adds the parameters
                fileNameBuilder.Append(restOfUri, indexOfParams, restOfUri.Length - indexOfParams);
                //Adds the filename extension.
                fileNameBuilder.Append(restOfUri, indexOfDot, indexOfParams - indexOfDot);
            }
        }

        var validFileName = GetValidFileName(fileNameBuilder.ToString());
        if (validFileName.Length > 25)
            validFileName = validFileName.Substring(0, 24) + validFileName.Substring(24).GetHashCode() + Path.GetExtension(validFileName);

        if (_tempPath == null)
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "HtmlRenderer");
            if (!Directory.Exists(_tempPath))
                Directory.CreateDirectory(_tempPath);
        }

        return new FileInfo(Path.Combine(_tempPath, validFileName));
    }

    public static int GetNextSubString(string str, int idx, out int length)
    {
        while (idx < str.Length && char.IsWhiteSpace(str[idx]))
            idx++;

        if (idx < str.Length)
        {
            var endIdx = idx + 1;
            while (endIdx < str.Length && !char.IsWhiteSpace(str[endIdx]))
                endIdx++;
            length = endIdx - idx;
            return idx;
        }

        length = 0;
        return -1;
    }

    public static bool SubStringEquals(string str, int idx, int length, string str2)
    {
        if (length != str2.Length || idx + length > str.Length)
            return false;

        for (int i = 0; i < length; i++)
        {
            if (char.ToLowerInvariant(str[idx + i]) != char.ToLowerInvariant(str2[i]))
                return false;
        }

        return true;
    }

    private static string GetValidFileName(string source)
    {
        string retVal = source;
        char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

        foreach (var invalidFileNameChar in invalidFileNameChars)
            retVal = retVal.Replace(invalidFileNameChar, '_');

        return retVal;
    }

    public static string ConvertToAlphaNumber(int number, string style = CssConstants.UpperAlpha)
    {
        if (number == 0)
            return string.Empty;

        if (style.Equals(CssConstants.LowerGreek, StringComparison.InvariantCultureIgnoreCase))
        {
            return ConvertToGreekNumber(number);
        }
        else if (style.Equals(CssConstants.LowerRoman, StringComparison.InvariantCultureIgnoreCase))
        {
            return ConvertToRomanNumbers(number, true);
        }
        else if (style.Equals(CssConstants.UpperRoman, StringComparison.InvariantCultureIgnoreCase))
        {
            return ConvertToRomanNumbers(number, false);
        }
        else if (style.Equals(CssConstants.Armenian, StringComparison.InvariantCultureIgnoreCase))
        {
            return ConvertToSpecificNumbers(number, _armenianDigitsTable);
        }
        else if (style.Equals(CssConstants.Georgian, StringComparison.InvariantCultureIgnoreCase))
        {
            return ConvertToSpecificNumbers(number, _georgianDigitsTable);
        }
        else if (style.Equals(CssConstants.Hebrew, StringComparison.InvariantCultureIgnoreCase))
        {
            return ConvertToSpecificNumbers(number, _hebrewDigitsTable);
        }
        else if (style.Equals(CssConstants.Hiragana, StringComparison.InvariantCultureIgnoreCase) || style.Equals(CssConstants.HiraganaIroha, StringComparison.InvariantCultureIgnoreCase))
        {
            return ConvertToSpecificNumbers2(number, _hiraganaDigitsTable);
        }
        else if (style.Equals(CssConstants.Katakana, StringComparison.InvariantCultureIgnoreCase) || style.Equals(CssConstants.KatakanaIroha, StringComparison.InvariantCultureIgnoreCase))
        {
            return ConvertToSpecificNumbers2(number, _satakanaDigitsTable);
        }
        else
        {
            var lowercase = style.Equals(CssConstants.LowerAlpha, StringComparison.InvariantCultureIgnoreCase) || style.Equals(CssConstants.LowerLatin, StringComparison.InvariantCultureIgnoreCase);
            return ConvertToEnglishNumber(number, lowercase);
        }
    }

    private static string ConvertToEnglishNumber(int number, bool lowercase)
    {
        var sb = string.Empty;
        int alphStart = lowercase ? 97 : 65;

        while (number > 0)
        {
            var n = number % 26 - 1;
            if (n >= 0)
            {
                sb = (char)(alphStart + n) + sb;
                number = number / 26;
            }
            else
            {
                sb = (char)(alphStart + 25) + sb;
                number = (number - 1) / 26;
            }
        }

        return sb;
    }

    private static string ConvertToGreekNumber(int number)
    {
        var sb = string.Empty;
        while (number > 0)
        {
            var n = number % 24 - 1;
            if (n > 16)
                n++;
            if (n >= 0)
            {
                sb = (char)(945 + n) + sb;
                number = number / 24;
            }
            else
            {
                sb = (char)(945 + 24) + sb;
                number = (number - 1) / 25;
            }
        }

        return sb;
    }

    private static string ConvertToRomanNumbers(int number, bool lowercase)
    {
        var sb = string.Empty;
        for (int i = 1000, j = 3; i > 0; i /= 10, j--)
        {
            int digit = number / i;
            sb += string.Format(_romanDigitsTable[j, digit]);
            number -= digit * i;
        }
        return lowercase ? sb.ToLower() : sb;
    }

    private static string ConvertToSpecificNumbers(int number, string[,] alphabet)
    {
        int level = 0;
        var sb = string.Empty;

        while (number > 0 && level < alphabet.GetLength(0))
        {
            var n = number % 10;
            if (n > 0)
                sb = alphabet[level, number % 10 - 1].ToString(CultureInfo.InvariantCulture) + sb;
            number /= 10;
            level++;
        }
        return sb;
    }

    private static string ConvertToSpecificNumbers2(int number, string[] alphabet)
    {
        for (int i = 20; i > 0; i--)
        {
            if (number > 49 * i - i + 1)
                number++;
        }

        var sb = string.Empty;
        while (number > 0)
        {
            sb = alphabet[Math.Max(0, number % 49 - 1)].ToString(CultureInfo.InvariantCulture) + sb;
            number /= 49;
        }
        return sb;
    }
}