using ServerPanel.Services;

namespace ServerPanel.Tests;

public class CrashLogParserTests
{
    // Log real capturado el 2026-07-23 en el crash de producción (mg_saw_v64_cs2).
    const string RealCrashLog = """
        Bad key for entity "l_5": Invalid parsed value for field "dissolvetype" of type '<type name is DEBUG only>' ('None')!
            Error parsing string 'None' as int
        ERROR: Error parsing string 'kRenderTransColor' as int
        Encountered bad enum value "kRenderTransColor" for type RenderMode_t, entity "jigsawface" field "rendermode"!
        WARNING: resource 'sprites/laserbeam.spr' is an unrecognized resource type!
        SV:  Spawn Server: mg_saw_v64_cs2
        CSource2Server::ApplyGameSettings game settings payload received:
        CNetworkGameServerBase::SetServerState (ss_waitingforgamesessionmanifest -> ss_loading)
        src/clientdll/contentupdatecontext.cpp (2036) : Staging library folder not found
        CNetworkGameServerBase::SetServerState (ss_loading -> ss_active)
        SV:  CNetworkServerService::FinishChangeLevel:  reactivating previous clients
        Created physics for mg_saw_v64_cs2
        CNavGenParams - Nav mesh requests project default generation parameters but actual parameters
          used during construction differ from defaults. Please re-export the map.
        src/clientdll/contentupdatecontext.cpp (2036) : Staging library folder not found
        WatchDog! Server took too long to process (probably infinite loop)
        FATAL ERROR: Watchdog timeout exceeded, exiting
         0 FATAL ERROR: Watchdog timeout exceeded, exiting
        """;

    [Fact]
    public void ExtractsMapName_FromRealCrashLog()
    {
        var found = CrashLogParser.TryExtractCrashedMap(RealCrashLog, out var mapName);

        Assert.True(found);
        Assert.Equal("mg_saw_v64_cs2", mapName);
    }

    [Fact]
    public void ExtractsLastMap_WhenMultipleMapsLoadedBeforeCrash()
    {
        var log = """
            Created physics for de_dust2
            some noise in between
            Created physics for mg_lego_islands
            WatchDog! Server took too long to process (probably infinite loop)
            FATAL ERROR: Watchdog timeout exceeded, exiting
            """;

        var found = CrashLogParser.TryExtractCrashedMap(log, out var mapName);

        Assert.True(found);
        Assert.Equal("mg_lego_islands", mapName);
    }

    [Fact]
    public void DoesNotMatch_WhenWatchdogSignatureIsAbsent()
    {
        // Un pod puede caerse por muchos otros motivos (OOM, error normal de salida, etc.)
        // — no debe registrarse como "mapa roto" salvo que sea exactamente esta firma.
        var log = """
            Created physics for mg_saw_v64_cs2
            Server shutting down normally.
            """;

        var found = CrashLogParser.TryExtractCrashedMap(log, out _);

        Assert.False(found);
    }

    [Fact]
    public void DoesNotMatch_WhenCreatedPhysicsLineIsAbsent()
    {
        var log = """
            WatchDog! Server took too long to process (probably infinite loop)
            FATAL ERROR: Watchdog timeout exceeded, exiting
            """;

        var found = CrashLogParser.TryExtractCrashedMap(log, out _);

        Assert.False(found);
    }
}
