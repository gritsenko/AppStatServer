Console.WriteLine("Test sentry");

Task.Run(async () =>
{
    using (SentrySdk.Init(o =>
           {
               o.Dsn = "https://5e79a97ae19d4187becbc9e4cdf2de52@localhost:7019/1";
               o.SendClientReports = false;
               o.AutoSessionTracking = true;
               o.StackTraceMode = StackTraceMode.Enhanced;
               o.IsGlobalModeEnabled = true;
               //o.Debug = true;
               o.ReportAssembliesMode = ReportAssembliesMode.None;
               //o.RequestBodyCompressionLevel = System.IO.Compression.CompressionLevel.NoCompression;
           }))
    {
        SentrySdk.ConfigureScope(s => s.User.Id = Guid.NewGuid().ToString());

        var rnd = new Random();
        await Parallel.ForAsync(0, 1, async (j, ct) =>
        {
            for (int i = 0; i < 50; i++)
            {
                var msg = "Console test error";
                SentrySdk.CaptureException(new Exception(msg));
                Console.WriteLine(msg);
                await Task.Delay(rnd.Next(10,40));
            }
        });

        Console.WriteLine("Finished");
    }
});

Console.ReadLine();