namespace StudentApi.Models;

public static class ImageData
{
    public static bool TryDecode(string? image, out string base64)
    {
        base64 = "";
        if (string.IsNullOrWhiteSpace(image) || image.Length > 550000)
            return false;

        var png = image.StartsWith("data:image/png;base64,", StringComparison.Ordinal);
        var jpeg = image.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal);
        if (!png && !jpeg)
            return false;

        try
        {
            var bytes = Convert.FromBase64String(image[(image.IndexOf(',') + 1)..]);
            if (bytes.Length == 0 || bytes.Length > 400 * 1024)
                return false;

            var valid = png
                ? bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                : bytes.Length >= 3 && bytes[0] == 0xFF
                    && bytes[1] == 0xD8 && bytes[2] == 0xFF;

            if (!valid)
                return false;

            base64 = Convert.ToBase64String(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
