using Framelet.Recording;

if (args is not ["--stdio"])
{
    Console.Error.WriteLine("This is Framelet's private recording worker. Start a recording from the Framelet application.");
    return 2;
}
return await NativeRecorderHost.RunAsync();
