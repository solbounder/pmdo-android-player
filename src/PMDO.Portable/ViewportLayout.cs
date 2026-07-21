namespace PMDO.Portable;

public sealed record ViewportPlacement(int Left, int Top, int Width, int Height, int Scale);

public sealed record AndroidViewportPlacement(
    int Left,
    int Top,
    int Width,
    int Height,
    int RenderScale,
    float ScaleX,
    float ScaleY);

public static class ViewportLayout
{
    public static ViewportPlacement Calculate(int availableWidth, int availableHeight, int logicalWidth, int logicalHeight, int maximumScale = 4)
    {
        if (availableWidth <= 0 || availableHeight <= 0 || logicalWidth <= 0 || logicalHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        int scale = Math.Clamp(Math.Min(availableWidth / logicalWidth, availableHeight / logicalHeight), 1, maximumScale);
        int width = logicalWidth * scale;
        int height = logicalHeight * scale;
        return new ViewportPlacement((availableWidth - width) / 2, (availableHeight - height) / 2, width, height, scale);
    }

    public static AndroidViewportPlacement CalculateAndroid(
        int availableWidth,
        int availableHeight,
        int logicalWidth,
        int logicalHeight,
        int mode)
    {
        if (availableWidth <= 0 || availableHeight <= 0 || logicalWidth <= 0 || logicalHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));

        mode = Math.Clamp(mode, 0, 5);
        float maximumFit = Math.Min(
            availableWidth / (float)logicalWidth,
            availableHeight / (float)logicalHeight);
        int renderScale = Math.Clamp((int)Math.Floor(maximumFit), 1, 8);
        if (mode is > 0 and < 5)
            renderScale = Math.Min(mode, renderScale);

        int width = logicalWidth * renderScale;
        int height = logicalHeight * renderScale;
        float scaleX = 1f;
        float scaleY = 1f;
        if (mode == 0)
        {
            scaleX = availableWidth / (float)width;
            scaleY = availableHeight / (float)height;
        }
        else if (mode == 5)
        {
            float aspectScale = Math.Min(
                availableWidth / (float)width,
                availableHeight / (float)height);
            scaleX = aspectScale;
            scaleY = aspectScale;
        }

        int visualWidth = (int)Math.Round(width * scaleX);
        int visualHeight = (int)Math.Round(height * scaleY);
        return new AndroidViewportPlacement(
            (availableWidth - visualWidth) / 2,
            (availableHeight - visualHeight) / 2,
            width,
            height,
            renderScale,
            scaleX,
            scaleY);
    }
}
