using Broiler.HTML.Core.Core.Dom;
using Broiler.HTML.CSS.Parse;
using Broiler.HTML.Utils;
using System;
using System.Globalization;

namespace Broiler.HTML.CSS.Dom;

internal sealed class CssLength
{
    private readonly double _number;

    public CssLength(string length)
    {
        Length = length;
        _number = 0f;
        Unit = CssUnit.None;
        IsPercentage = false;

        //Return zero if no length specified, zero specified
        if (string.IsNullOrEmpty(length) || length == "0")
            return;

        //If percentage, use ParseNumber
        if (length.EndsWith('%'))
        {
            _number = CssValueParser.ParseNumber(length, 1);
            IsPercentage = true;
            return;
        }

        //If no units, has error
        if (length.Length < 3)
        {
            _ = double.TryParse(length, out _number);
            HasError = true;
            return;
        }

        // Check for 4-character units (e.g. "vmin", "vmax")
        if (length.Length >= 5)
        {
            var last4 = length.Substring(length.Length - 4, 4);
            if (last4.Equals(CssConstants.Vmin, StringComparison.OrdinalIgnoreCase))
            {
                Unit = CssUnit.Vmin;
                IsRelative = true;
                string vmNumber = length[..^4];
                if (!double.TryParse(vmNumber, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out _number))
                    HasError = true;
                return;
            }
            if (last4.Equals(CssConstants.Vmax, StringComparison.OrdinalIgnoreCase))
            {
                Unit = CssUnit.Vmax;
                IsRelative = true;
                string vmNumber = length[..^4];
                if (!double.TryParse(vmNumber, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out _number))
                    HasError = true;
                return;
            }
        }

        // Check for 3-character units first (e.g. "rem")
        if (length.Length >= 4 && length.EndsWith(CssConstants.Rem, StringComparison.Ordinal))
        {
            Unit = CssUnit.Rem;
            IsRelative = true;
            string remNumber = length[..^3];
            if (!double.TryParse(remNumber, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out _number))
                HasError = true;
            return;
        }

        //Get units of the length
        string u = length.Substring(length.Length - 2, 2);

        //Number of the length
        string number = length[..^2];

        //TODO: Units behave different in paper and in screen!
        switch (u)
        {
            case CssConstants.Em:
                Unit = CssUnit.Ems;
                IsRelative = true;
                break;
            case CssConstants.Ex:
                Unit = CssUnit.Ex;
                IsRelative = true;
                break;
            case CssConstants.Ch:
                Unit = CssUnit.Ch;
                IsRelative = true;
                break;
            case CssConstants.Ic:
                Unit = CssUnit.Ic;
                IsRelative = true;
                break;
            case CssConstants.Px:
                Unit = CssUnit.Pixels;
                IsRelative = true;
                break;
            case CssConstants.Mm:
                Unit = CssUnit.Milimeters;
                break;
            case CssConstants.Cm:
                Unit = CssUnit.Centimeters;
                break;
            case CssConstants.In:
                Unit = CssUnit.Inches;
                break;
            case CssConstants.Pt:
                Unit = CssUnit.Points;
                break;
            case CssConstants.Pc:
                Unit = CssUnit.Picas;
                break;
            default:
                // Check for viewport units (case-insensitive)
                if (u.Equals(CssConstants.Vh, StringComparison.OrdinalIgnoreCase))
                {
                    Unit = CssUnit.Vh;
                    IsRelative = true;
                    break;
                }
                if (u.Equals(CssConstants.Vw, StringComparison.OrdinalIgnoreCase))
                {
                    Unit = CssUnit.Vw;
                    IsRelative = true;
                    break;
                }
                HasError = true;
                return;
        }

        if (!double.TryParse(number, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out _number))
            HasError = true;
    }


    public double Number => _number;
    public bool HasError { get; }
    public bool IsPercentage { get; }
    public bool IsRelative { get; }
    public CssUnit Unit { get; }
    public string Length { get; }

    public CssLength ConvertEmToPoints(double emSize)
    {
        if (HasError)
            throw new InvalidOperationException("Invalid length");

        if (Unit != CssUnit.Ems)
            throw new InvalidOperationException("Length is not in ems");

        return new CssLength($"{Convert.ToSingle(Number * emSize).ToString("0.0", NumberFormatInfo.InvariantInfo)}pt");
    }

    public CssLength ConvertEmToPixels(double pixelFactor)
    {
        if (HasError)
            throw new InvalidOperationException("Invalid length");

        if (Unit != CssUnit.Ems)
            throw new InvalidOperationException("Length is not in ems");

        return new CssLength($"{Convert.ToSingle(Number * pixelFactor).ToString("0.0", NumberFormatInfo.InvariantInfo)}px");
    }

    public override string ToString()
    {
        if (HasError)
        {
            return string.Empty;
        }
        else if (IsPercentage)
        {
            return $"{Number}%";
        }
        else
        {
            string u = string.Empty;

            switch (Unit)
            {
                case CssUnit.None:
                    break;
                case CssUnit.Ems:
                    u = "em";
                    break;
                case CssUnit.Pixels:
                    u = "px";
                    break;
                case CssUnit.Ex:
                    u = "ex";
                    break;
                case CssUnit.Ch:
                    u = "ch";
                    break;
                case CssUnit.Ic:
                    u = "ic";
                    break;
                case CssUnit.Inches:
                    u = "in";
                    break;
                case CssUnit.Centimeters:
                    u = "cm";
                    break;
                case CssUnit.Milimeters:
                    u = "mm";
                    break;
                case CssUnit.Points:
                    u = "pt";
                    break;
                case CssUnit.Picas:
                    u = "pc";
                    break;
                case CssUnit.Rem:
                    u = "rem";
                    break;
                case CssUnit.Vh:
                    u = "vh";
                    break;
                case CssUnit.Vw:
                    u = "vw";
                    break;
                case CssUnit.Vmin:
                    u = "vmin";
                    break;
                case CssUnit.Vmax:
                    u = "vmax";
                    break;
            }

            return $"{Number:0.0}{u}".Replace(',','.');
        }
    }
}
