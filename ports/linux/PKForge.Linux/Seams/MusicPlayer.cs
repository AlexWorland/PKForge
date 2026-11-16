using PKForge.Domain;

namespace PKForge.App.Platforms.Android;

// Stand-in for src/PKForge.App/Platforms/Android/MusicPlayer.cs (Android MediaPlayer). The
// library is always empty and every command does nothing.
public sealed class MusicPlayer : IMusicPlayer, IDisposable
{
    public IReadOnlyList<MusicTrack> Library => [];
    public bool IsPlaying => false;
    public bool IsOn => false;
    public int? CurrentIndex => null;
    public MusicOrder Order => default;
    public MusicPlace Place => default;
    public bool Autostart => false;
    public int Volume => MusicPlaces.DefaultVolume;
    public string? LastError => null;

    public int Add(IReadOnlyList<PickedDocument> documents) => 0;
    public void Remove(int index) { }
    public void Clear() { }
    public void Play() { }
    public void Pause() { }
    public void Skip() { }
    public void SetOrder(MusicOrder order) { }
    public void SetAutostart(bool on) { }
    public MusicSource SourceFor(MusicPlace place) => MusicSource.Parse("");
    public void SetSource(MusicPlace place, MusicSource source) { }
    public void EnterPlace(MusicPlace place) { }
    public void SetVolume(int volume) { }
    public void PauseForBackground() { }
    public void ResumeFromBackground() { }
    public void MaybeAutostart() { }
    public void Dispose() { }
}
