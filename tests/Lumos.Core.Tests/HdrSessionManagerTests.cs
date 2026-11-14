using Lumos.Core;
using Xunit;

namespace Lumos.Core.Tests;

public class HdrSessionManagerTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);
    private const string Game = @"C:\Games\Foo\Foo.exe";
    private const string Other = @"C:\Games\Bar\Bar.exe";

    private readonly FakeHdr _hdr = new FakeHdr().Add("main", primary: true).Add("side");
    private readonly FakeTimers _timers = new();
    private readonly FakeState _state = new();

    private HdrSessionManager Create(params WatchedApp[] apps)
    {
        var m = new HdrSessionManager(_hdr, _state, _timers);
        m.ApplySettings(new LumosSettings { Apps = [.. apps], GraceDelaySeconds = 3 });
        return m;
    }

    private static WatchedApp App(string path, DisplayTargetKind target = DisplayTargetKind.Default, string? displayId = null) =>
        new() { DisplayName = Path.GetFileNameWithoutExtension(path), ExePath = path, Target = target, TargetDisplayId = displayId };

    [Fact]
    public void Turns_hdr_on_for_primary_by_default_and_off_after_grace()
    {
        using var m = Create(App(Game));

        Assert.True(m.TryStartProcessSession(100, Game));
        Assert.True(_hdr.IsOn("main"));
        Assert.False(_hdr.IsOn("side"));
        Assert.Equal(["main"], _state.Owned);

        m.OnProcessExited(100);
        Assert.True(_hdr.IsOn("main")); // grace period
        _timers.Advance(Grace);
        Assert.False(_hdr.IsOn("main"));
        Assert.Empty(_state.Owned);
    }

    [Fact]
    public void Ignores_unwatched_processes_and_path_mismatches()
    {
        using var m = Create(App(Game));
        Assert.False(m.IsWatchedFileName("notepad.exe"));
        Assert.True(m.IsWatchedFileName("FOO.EXE"));
        Assert.False(m.TryStartProcessSession(1, @"C:\Elsewhere\Foo.exe"));
        Assert.Empty(_hdr.Calls);
    }

    [Fact]
    public void Match_by_name_only_accepts_any_folder()
    {
        var app = App(Game);
        app.MatchByNameOnly = true;
        using var m = Create(app);
        Assert.True(m.TryStartProcessSession(1, @"D:\Other\foo.exe"));
        Assert.True(_hdr.IsOn("main"));
    }

    [Fact]
    public void Leaves_hdr_alone_if_it_was_already_on()
    {
        _hdr.UserSet("main", true);
        using var m = Create(App(Game));

        m.TryStartProcessSession(100, Game);
        m.OnProcessExited(100);
        _timers.Advance(Grace);

        Assert.True(_hdr.IsOn("main"));
        Assert.Empty(_hdr.Calls);
    }

    [Fact]
    public void Restart_within_grace_period_keeps_hdr_on_without_flicker()
    {
        using var m = Create(App(Game));
        m.TryStartProcessSession(100, Game);
        m.OnProcessExited(100);
        _timers.Advance(TimeSpan.FromSeconds(1));
        m.TryStartProcessSession(101, Game);
        _timers.Advance(Grace * 2);

        Assert.True(_hdr.IsOn("main"));
        Assert.Equal([("main", true)], _hdr.Calls);

        m.OnProcessExited(101);
        _timers.Advance(Grace);
        Assert.False(_hdr.IsOn("main"));
    }

    [Fact]
    public void Stays_on_until_all_watched_apps_exit()
    {
        using var m = Create(App(Game), App(Other));
        m.TryStartProcessSession(1, Game);
        m.TryStartProcessSession(2, Other);
        m.OnProcessExited(1);
        _timers.Advance(Grace);
        Assert.True(_hdr.IsOn("main"));

        m.OnProcessExited(2);
        _timers.Advance(Grace);
        Assert.False(_hdr.IsOn("main"));
    }

    [Fact]
    public void Respects_user_turning_hdr_off_mid_session()
    {
        using var m = Create(App(Game));
        m.TryStartProcessSession(1, Game);
        _hdr.UserSet("main", false);
        _hdr.Calls.Clear();

        m.OnProcessExited(1);
        _timers.Advance(Grace);

        Assert.Empty(_hdr.Calls);
        Assert.Empty(_state.Owned);
    }

    [Fact]
    public void Recovers_stale_ownership_after_crash()
    {
        _hdr.UserSet("main", true);
        _state.Owned = ["main"];
        using var m = Create(App(Game));

        m.RecoverStaleOwnership();

        Assert.False(_hdr.IsOn("main"));
        Assert.Empty(_state.Owned);
    }

    [Fact]
    public void Keeps_recovered_ownership_while_app_still_running()
    {
        _hdr.UserSet("main", true);
        _state.Owned = ["main"];
        using var m = Create(App(Game));

        m.TryStartProcessSession(1, Game); // found by the initial scan
        m.RecoverStaleOwnership();
        Assert.True(_hdr.IsOn("main"));

        m.OnProcessExited(1);
        _timers.Advance(Grace);
        Assert.False(_hdr.IsOn("main"));
    }

    [Fact]
    public void Specific_display_target_switches_only_that_display()
    {
        using var m = Create(App(Game, DisplayTargetKind.Specific, "side"));
        m.TryStartProcessSession(1, Game);
        Assert.True(_hdr.IsOn("side"));
        Assert.False(_hdr.IsOn("main"));
    }

    [Fact]
    public void Specific_display_falls_back_to_primary_when_disconnected()
    {
        using var m = Create(App(Game, DisplayTargetKind.Specific, "unplugged"));
        m.TryStartProcessSession(1, Game);
        Assert.True(_hdr.IsOn("main"));
    }

    [Fact]
    public void All_hdr_capable_skips_unsupported_displays()
    {
        _hdr.Add("sdr-only", supported: false);
        using var m = Create(App(Game, DisplayTargetKind.AllHdrCapable));
        m.TryStartProcessSession(1, Game);
        Assert.True(_hdr.IsOn("main"));
        Assert.True(_hdr.IsOn("side"));
        Assert.False(_hdr.IsOn("sdr-only"));
    }

    [Fact]
    public void Launch_hold_keeps_hdr_on_until_process_appears_then_follows_process()
    {
        var app = App(Game);
        using var m = Create(app);

        Assert.Equal(["main"], m.BeginLaunch(app));
        Assert.True(_hdr.IsOn("main"));

        _timers.Advance(TimeSpan.FromSeconds(10));
        m.TryStartProcessSession(7, Game);
        _timers.Advance(TimeSpan.FromSeconds(120)); // hold timeout must not fire any more
        Assert.True(_hdr.IsOn("main"));

        m.OnProcessExited(7);
        _timers.Advance(Grace);
        Assert.False(_hdr.IsOn("main"));
    }

    [Fact]
    public void Launch_hold_expires_if_process_never_appears()
    {
        var app = App(Game);
        using var m = Create(app);
        m.BeginLaunch(app);
        _timers.Advance(TimeSpan.FromSeconds(60));
        _timers.Advance(Grace);
        Assert.False(_hdr.IsOn("main"));
    }

    [Fact]
    public void Pause_restores_sdr_and_resume_reapplies()
    {
        using var m = Create(App(Game));
        m.TryStartProcessSession(1, Game);

        m.SetPaused(true);
        Assert.False(_hdr.IsOn("main"));

        m.SetPaused(false);
        Assert.True(_hdr.IsOn("main"));
    }

    [Fact]
    public void Process_started_while_paused_is_tracked_and_applied_on_resume()
    {
        using var m = Create(App(Game));
        m.SetPaused(true);
        Assert.True(m.TryStartProcessSession(1, Game));
        Assert.False(_hdr.IsOn("main"));
        m.SetPaused(false);
        Assert.True(_hdr.IsOn("main"));
    }

    [Fact]
    public void Manual_off_drops_ownership()
    {
        using var m = Create(App(Game));
        m.TryStartProcessSession(1, Game);
        m.SetHdrManually("main", false);
        Assert.Empty(_state.Owned);

        m.SetHdrManually("main", true); // user wants it on: not ours to turn off
        m.OnProcessExited(1);
        _timers.Advance(Grace);
        Assert.True(_hdr.IsOn("main"));
    }

    [Fact]
    public void Dispose_restores_sdr_on_owned_displays_only()
    {
        _hdr.UserSet("side", true);
        var m = Create(App(Game));
        m.TryStartProcessSession(1, Game);
        m.Dispose();
        Assert.False(_hdr.IsOn("main"));
        Assert.True(_hdr.IsOn("side"));
    }

    [Fact]
    public void Disabled_apps_are_not_watched()
    {
        var app = App(Game);
        app.Enabled = false;
        using var m = Create(app);
        Assert.False(m.IsWatchedFileName("Foo.exe"));
    }

    [Fact]
    public void Status_reports_sessions_and_ownership()
    {
        using var m = Create(App(Game));
        m.TryStartProcessSession(42, Game);
        var status = m.GetStatus();
        var session = Assert.Single(status.Sessions);
        Assert.Equal(42, session.ProcessId);
        Assert.True(status.Displays.Single(d => d.Id == "main").OwnedByLumos);
        Assert.False(status.Displays.Single(d => d.Id == "side").OwnedByLumos);
    }
}
