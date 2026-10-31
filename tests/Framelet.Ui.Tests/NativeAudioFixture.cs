using Framelet.Recording;

internal static class NativeAudioFixture
{
    internal static readonly IReadOnlyList<NativeAudioDevice> Outputs = new[]
    {
        new NativeAudioDevice("fixture-speakers", "Fixture speakers", true),
        new NativeAudioDevice("fixture-headphones", "Fixture headphones", false)
    };

    internal static readonly IReadOnlyList<NativeAudioDevice> Inputs = new[]
    {
        new NativeAudioDevice("fixture-microphone", "Fixture microphone", true),
        new NativeAudioDevice("fixture-headset", "Fixture headset microphone", false)
    };

    internal static IReadOnlyList<NativeAudioDevice> List(bool microphone) => microphone ? Inputs : Outputs;
}
