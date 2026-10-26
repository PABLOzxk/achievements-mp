using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AchievementsMP.Data.Repositories;

namespace AchievementsMP.App.Services;

public sealed class InvalidAvatarException(string message) : Exception(message);

/// <summary>
/// Foto de perfil escolhida pelo usuário. Qualquer imagem que o Windows saiba abrir vira um quadrado de até
/// <see cref="Size"/> px em PNG, cortado no centro e girado conforme a orientação gravada pela câmera do celular.
/// Assim o banco (e o backup) guarda algumas dezenas de KB, e não a foto original.
/// </summary>
public static class AvatarImage
{
    public const int Size = 256;
    private const long MaxFileBytes = 50L * 1024 * 1024;
    /// <summary>Uma foto guardada maior que isso não foi feita pelo app (banco adulterado): não é aberta.</summary>
    private const int MaxStoredSide = 4096;

    public static byte[] FromFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
                throw new InvalidAvatarException("A imagem tem mais de 50 MB. Escolha uma menor.");

            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];

            var side = Math.Min(frame.PixelWidth, frame.PixelHeight);
            if (side < 1)
                throw new InvalidAvatarException("A imagem está vazia.");
            BitmapSource square = new CroppedBitmap(frame, new Int32Rect((frame.PixelWidth - side) / 2, (frame.PixelHeight - side) / 2, side, side));
            if (side > Size)
                square = new TransformedBitmap(square, new ScaleTransform((double)Size / side, (double)Size / side));
            // O recorte é no centro, então girar depois de cortar dá o mesmo quadrado.
            if (CameraRotation(frame) is var angle and not 0)
                square = new TransformedBitmap(square, new RotateTransform(angle));

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(square, PixelFormats.Bgra32, null, 0)));
            using var output = new MemoryStream();
            encoder.Save(output);
            if (output.Length > ProfileRepository.MaxAvatarBytes)
                throw new InvalidAvatarException("Não consegui reduzir essa imagem. Tente outra.");
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException or OverflowException)
        {
            throw new InvalidAvatarException("O Windows não conseguiu abrir essa imagem. Tente um arquivo PNG ou JPG.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidAvatarException($"Não consegui ler o arquivo: {ex.Message}");
        }
    }

    /// <summary>Foto guardada no banco, pronta para exibir. Nula se não abrir.</summary>
    public static BitmapSource? Load(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
            return null;
        try
        {
            var size = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if (size.PixelWidth > MaxStoredSide || size.PixelHeight > MaxStoredSide)
                return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
                                       or IOException or System.Runtime.InteropServices.COMException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Graus (sentido horário) para deixar a foto em pé, pela marca de orientação EXIF (1 a 8). As variantes espelhadas
    /// (raras) só são giradas: ficam em pé, mas espelhadas.
    /// </summary>
    private static double CameraRotation(BitmapFrame frame)
    {
        try
        {
            return (frame.Metadata as BitmapMetadata)?.GetQuery("System.Photo.Orientation") switch
            {
                ushort and (3 or 4) => 180,
                ushort and (5 or 6) => 90,
                ushort and (7 or 8) => 270,
                _ => 0,
            };
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return 0; // formato sem metadados (BMP, por exemplo)
        }
    }
}
