namespace FiscalPlatform.Domain;

/// <summary>Validação de CPF/CNPJ e utilitários de dígitos.</summary>
public static class DocumentNumbers
{
    public static string Digits(string? value) =>
        value is null ? "" : new string(value.Where(char.IsDigit).ToArray());

    public static bool IsDigits(string? value, params int[] lengths) =>
        value is not null && value.All(char.IsDigit) && lengths.Contains(value.Length);

    public static bool IsValidCpfOrCnpj(string? value)
    {
        var d = Digits(value);
        return d.Length == 11 ? IsValidCpf(d) : d.Length == 14 && IsValidCnpj(d);
    }

    public static bool IsValidCpf(string? value)
    {
        var d = Digits(value);
        if (d.Length != 11 || d.Distinct().Count() == 1) return false;
        return CheckDigit(d[..9], [10, 9, 8, 7, 6, 5, 4, 3, 2]) == d[9] - '0'
            && CheckDigit(d[..10], [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]) == d[10] - '0';
    }

    public static bool IsValidCnpj(string? value)
    {
        var d = Digits(value);
        if (d.Length != 14 || d.Distinct().Count() == 1) return false;
        return CheckDigit(d[..12], [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == d[12] - '0'
            && CheckDigit(d[..13], [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == d[13] - '0';
    }

    private static int CheckDigit(string digits, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++) sum += (digits[i] - '0') * weights[i];
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    public static string Format(string? value)
    {
        var d = Digits(value);
        return d.Length switch
        {
            11 => $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            14 => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            _ => value ?? "",
        };
    }
}
