using System.Windows.Controls;
using NAudio.Wave;
using boardzz.audio;

namespace boardzz;

internal sealed class padSlot(Button button)
{
    public Button button { get; } = button;

    public string? filePath;
    public string name = "";
    public string? emoji;
    public double startSeconds;
    public double endSeconds;      // 0 = play to the end of the file
    public float volume = 1f;

    public padVoice? voice;
    public AudioFileReader? reader;
    public bool isPlaying => voice != null;

    public void reset()
    {
        filePath = null;
        name = "";
        emoji = null;
        startSeconds = 0;
        endSeconds = 0;
        volume = 1f;
    }
}
