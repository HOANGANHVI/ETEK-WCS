using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Config;
using WCS.Domain.Common;
using WCS.Domain.Locations;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>Configuration persistence against the real PostgreSQL schema (needs the database password, like the other integration tests).</summary>
[Collection("Database")]
public class ConfigIntegrationTests(DatabaseFixture fixture)
{
    private const string Secret = "Db-S3cret-Passw0rd";

    private static UpdateRcsCommand Rcs() => new(true, new RcsSettings("RCS", "rcs.example.test", 8182, null, ConnectionOptions.AuthUserPassword, "svc", 5000),
        Secret, false, null, false, "it");

    [Fact]
    public async Task Connection_settings_survive_a_new_scope_and_the_secret_is_encrypted_in_the_database()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.Scoped(async sp => await sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(Rcs()));
        await h.Scoped(async sp => await sp.GetRequiredService<ConnectionConfigService>().UpdatePlcAsync(new UpdatePlcCommand(false,
            new PlcSettings("PLC", "192.0.2.10", 5007, ConnectionOptions.PlcMcProtocol3EBinaryTcp, 0, 255, 0, 3000, null), "it")));

        var view = await h.Scoped(sp => sp.GetRequiredService<ConnectionConfigService>().GetRcsAsync());
        Assert.True(view.Configured); Assert.True(view.Secrets.HasPassword);
        Assert.Equal("rcs.example.test", view.Settings.Host);

        var raw = await h.Db(db => db.ConnectionSettings.AsNoTracking().SingleAsync(x => x.Kind == ConnectionKind.Rcs));
        Assert.DoesNotContain(Secret, raw.ProtectedSecrets);
        Assert.DoesNotContain(Secret, raw.SettingsJson);
        Assert.Equal(2, await h.Db(db => db.ConnectionSettings.CountAsync()));
    }

    [Fact]
    public async Task Check_result_is_persisted_with_the_simulated_default_probe()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.Scoped(async sp => await sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(Rcs()));
        var r = await h.Scoped(sp => sp.GetRequiredService<ConnectionConfigService>().TestAsync(ConnectionKind.Rcs));
        Assert.Equal(ConnectionTestOutcome.Simulated, r.Outcome);
        var view = await h.Scoped(sp => sp.GetRequiredService<ConnectionConfigService>().GetRcsAsync());
        Assert.Equal(ConnectionTestOutcome.Simulated, view.LastTest!.Outcome);
    }

    [Fact]
    public async Task Robot_code_is_unique_and_an_agv_with_an_active_task_cannot_be_disabled()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(); // AGV1 already has robot code R-FULL, AGV2 R-EMPTY
        var agv2 = await h.AgvAsync("AGV2");
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            h.Scoped(sp => sp.GetRequiredService<OperationalConfigService>().UpdateAgvAsync(agv2.Id, new UpdateAgvCommand("R-FULL", true))));
        Assert.Contains(ex.Errors, e => e.Field == "rcsRobotCode");

        await h.StockAsync("A1", RackKind.Full, 1);
        await h.CreateTaskAsync("BP01", "A1"); // AGV1 now owns an active task
        var agv1 = await h.AgvAsync("AGV1");
        var conflict = await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() =>
            h.Scoped(sp => sp.GetRequiredService<OperationalConfigService>().UpdateAgvAsync(agv1.Id, new UpdateAgvCommand("R-FULL", false))));
        Assert.Equal("AGV_HAS_ACTIVE_TASK", conflict.Code);
    }

    [Fact]
    public async Task Location_can_be_created_but_capacity_cannot_drop_below_what_is_stored_or_reserved()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 3);
        await h.StockAsync("F1", RackKind.Full, 2);

        var created = await h.Scoped(sp => sp.GetRequiredService<OperationalConfigService>()
            .SaveLocationAsync(new SaveLocationCommand("F9", "Vị trí thử", LocationType.Buffer, 2, AllowedRackKind.Full, true, false), true));
        Assert.Equal("F9", created.Code);
        await Assert.ThrowsAsync<ValidationFailedException>(() => h.Scoped(sp => sp.GetRequiredService<OperationalConfigService>()
            .SaveLocationAsync(new SaveLocationCommand("F9", "dup", LocationType.Buffer, 2, AllowedRackKind.Full, true, false), true)));

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => h.Scoped(sp => sp.GetRequiredService<OperationalConfigService>()
            .SaveLocationAsync(new SaveLocationCommand("F1", "F1", LocationType.Buffer, 1, AllowedRackKind.Full, true, false), false)));
        Assert.Contains(ex.Errors, e => e.Field == "rackCapacity");

        var ok = await h.Scoped(sp => sp.GetRequiredService<OperationalConfigService>()
            .SaveLocationAsync(new SaveLocationCommand("F1", "F1", LocationType.Buffer, 4, AllowedRackKind.Full, true, false), false));
        Assert.Equal(4, ok.RackCapacity);
    }

    [Fact]
    public async Task Operations_view_exposes_the_configured_flows_and_marks_unapproved_policies()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        var ops = await h.Scoped(sp => sp.GetRequiredService<OperationalConfigService>().GetAsync());
        Assert.Equal("PER_AGV", ops.Policy.Concurrency); Assert.True(ops.Policy.ConcurrencyApproved);
        Assert.False(ops.Policy.QueueOrderApproved);          // FIFO is a neutral default, NOT an approved priority policy
        Assert.Equal(5, ops.Flows.Count);
        Assert.DoesNotContain(ops.Flows, f => f.Flow.Contains("04"));
    }
}
