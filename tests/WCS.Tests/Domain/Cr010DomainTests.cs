using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;

namespace WCS.Tests.Domain;

/// <summary>CR-010 pure domain rules (no database): state transitions, port release rule, handshake recovery decisions, controlled conditions.</summary>
public class Cr010DomainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);

    private static StationTransaction NewTx(string? requestId = "R1") => StationTransaction.Create("A1", "PLC-01", "A1", StationOperation.Pickup, null, null, requestId, T0, RackKind.Full);

    // ------------------------------------------------------------------ Station Transaction

    [Fact]
    public void Station_transaction_completes_only_after_handover_and_after_the_AMR_left_the_port()
    {
        var tx = NewTx();
        tx.Advance(StationTransactionState.Requested, T0); tx.Advance(StationTransactionState.Accepted, T0); tx.Advance(StationTransactionState.AgvAssigned, T0);
        Assert.Throws<DomainException>(() => tx.Advance(StationTransactionState.Completed, T0));          // cannot skip handover and exit
        Assert.Throws<DomainException>(() => tx.Advance(StationTransactionState.AgvExited, T0));          // the AMR cannot leave before the handover is done
        tx.Advance(StationTransactionState.HandoverCompleted, T0);
        Assert.True(tx.IsActive);                                                                          // still holds the port
        Assert.Throws<DomainException>(() => tx.Advance(StationTransactionState.Completed, T0));
        tx.Advance(StationTransactionState.AgvExited, T0);
        tx.Advance(StationTransactionState.Completed, T0);
        Assert.False(tx.IsActive);
        Assert.NotNull(tx.FinishedAt);
    }

    [Fact]
    public void Station_transaction_never_moves_backwards_and_a_final_one_is_frozen()
    {
        var tx = NewTx();
        tx.Advance(StationTransactionState.AtStation, T0);
        Assert.Throws<DomainException>(() => tx.Advance(StationTransactionState.Requested, T0));
        tx.Fail("x", T0);
        Assert.False(tx.IsActive);
        Assert.Throws<DomainException>(() => tx.Advance(StationTransactionState.Handling, T0));
    }

    [Fact]
    public void Handshake_step_only_increases_and_must_be_1_to_9()
    {
        var tx = NewTx();
        Assert.True(tx.ObserveHandshakeStep(3, T0));
        Assert.False(tx.ObserveHandshakeStep(2, T0));
        Assert.Equal(3, tx.HandshakeStep);
        Assert.Throws<DomainException>(() => tx.ObserveHandshakeStep(0, T0));
        Assert.Throws<DomainException>(() => tx.ObserveHandshakeStep(10, T0));
    }

    [Fact]
    public void Request_ID_up_to_254_characters_is_accepted_and_longer_is_refused()
    {
        Assert.NotNull(NewTx(new string('x', 254)));
        Assert.Throws<DomainException>(() => NewTx(new string('x', 255)));
    }

    [Fact]
    public void A_transaction_in_reconciliation_still_holds_its_port_and_can_only_be_resumed_explicitly()
    {
        var tx = NewTx();
        tx.Advance(StationTransactionState.AgvAssigned, T0);
        tx.RequireReconciliation("RECON_X", T0);
        Assert.True(tx.IsActive);
        Assert.Throws<DomainException>(() => tx.Advance(StationTransactionState.AtStation, T0));
        tx.ResumeFromReconciliation(StationTransactionState.AgvAssigned, T0);
        Assert.Equal(StationTransactionState.AgvAssigned, tx.State);
    }

    [Fact]
    public void Suspending_for_a_PLC_link_loss_keeps_state_and_does_not_cancel()
    {
        var tx = NewTx();
        tx.Advance(StationTransactionState.AtStation, T0);
        tx.Suspend("PLC_LINK_LOST", T0);
        Assert.True(tx.Suspended);
        Assert.Equal(StationTransactionState.AtStation, tx.State);
        Assert.True(tx.IsActive);
        tx.ResumeLink(T0);
        Assert.False(tx.Suspended);
    }

    // ------------------------------------------------------------------ handshake recovery decision table

    private static PlcHandshakeObservation Obs(string? id, params int[] setSteps) =>
        new(id, Enumerable.Range(1, 9).ToDictionary(i => i, i => setSteps.Contains(i)));

    [Fact]
    public void Recovery_without_usable_data_decides_nothing()
    {
        Assert.Equal(RecoveryKind.NoData, HandshakeRecovery.Evaluate(NewTx(), null).Kind);
        Assert.Equal(RecoveryKind.NoData, HandshakeRecovery.Evaluate(NewTx(), Obs("R1", 1) with { QualityGood = false }).Kind);
    }

    [Fact]
    public void Recovery_resumes_when_the_PLC_progressed_and_does_nothing_when_nothing_changed()
    {
        var tx = NewTx(); tx.ObserveHandshakeStep(2, T0);
        Assert.Equal(RecoveryKind.NoChange, HandshakeRecovery.Evaluate(tx, Obs("R1", 1, 2)).Kind);
        var d = HandshakeRecovery.Evaluate(tx, Obs("R1", 1, 2, 3, 4));
        Assert.Equal((RecoveryKind.Resume, 4), (d.Kind, d.Step));
    }

    [Fact]
    public void Recovery_asks_for_reconciliation_when_the_PLC_shows_another_request_reset_or_regressed()
    {
        var tx = NewTx(); tx.ObserveHandshakeStep(4, T0);
        Assert.Equal("RECON_REQUEST_ID_CHANGED", HandshakeRecovery.Evaluate(tx, Obs("OTHER", 1, 2, 3, 4)).Code);
        Assert.Equal("RECON_PLC_RESET", HandshakeRecovery.Evaluate(tx, Obs("R1")).Code);
        Assert.Equal("RECON_BIT_REGRESSED", HandshakeRecovery.Evaluate(tx, Obs("R1", 1, 2)).Code);
    }

    [Fact]
    public void Recovery_never_touches_a_finished_transaction()
    {
        var tx = NewTx(); tx.Fail("x", T0);
        Assert.Equal(RecoveryKind.NoChange, HandshakeRecovery.Evaluate(tx, Obs("OTHER")).Kind);
    }

    // ------------------------------------------------------------------ order / leg state machines

    private static ProcessSnapshot Snap() => new(Guid.NewGuid(), "P", 1, Guid.NewGuid(), RequestType.PickupRequest, RackKind.Full, "s1",
        [new StepSnapshot("s1", 1, RouteStepType.ProcessCompletion, SourceMode.Request, DestinationMode.Request, DestinationResolution.BeforePick, null, null, WaitBehavior.Wait, ErrorBehavior.Fail, [], null, [])]);

    [Fact]
    public void Order_keeps_a_copy_of_its_process_and_obeys_its_state_table()
    {
        var o = TransportOrder.Create("k", Snap(), "A1", "t", T0);
        Assert.Equal("P", o.Snapshot.ProcessCode);
        o.TransitionTo(OrderState.Accepted, null, WaitKind.None, T0);
        o.TransitionTo(OrderState.Waiting, "w", WaitKind.AtBuffer, T0);
        Assert.Equal(WaitKind.AtBuffer, o.WaitKind);
        o.TransitionTo(OrderState.InProgress, null, WaitKind.None, T0);
        Assert.Equal(WaitKind.None, o.WaitKind);
        o.TransitionTo(OrderState.Completed, null, WaitKind.None, T0);
        Assert.True(o.IsTerminal);
        Assert.Throws<DomainException>(() => o.TransitionTo(OrderState.InProgress, null, WaitKind.None, T0));
    }

    [Fact]
    public void Leg_that_carries_a_rack_cannot_go_back_to_ready_and_cannot_drop_before_pick()
    {
        var leg = TransportLeg.Create(Guid.NewGuid(), 1, "s1", RouteStepType.Delivery, "A1", "B1", RackKind.Full, false, T0);
        leg.TransitionTo(LegState.Ready, null, WaitKind.None, T0);
        leg.AssignAgv(Guid.NewGuid(), T0);
        leg.TransitionTo(LegState.InProgress, null, WaitKind.None, T0);
        Assert.Throws<DomainException>(() => leg.ConfirmDrop(T0));
        leg.ConfirmPick(T0);
        Assert.True(leg.RackOnAgv);
        Assert.Throws<DomainException>(() => leg.TransitionTo(LegState.Ready, null, WaitKind.None, T0));
        leg.TransitionTo(LegState.Waiting, null, WaitKind.DestinationUnavailable_AgvHoldingRack, T0);
        Assert.True(leg.OccupiesAgv);                                                                      // a waiting AMR still holds its AGV
    }

    // ------------------------------------------------------------------ conditions are data, never code

    [Fact]
    public void Conditions_round_trip_as_typed_data_and_unknown_types_are_refused()
    {
        var json = ConditionJson.Write([new ConditionSpec(ConditionType.RackTypeMatch, "Full", "B1"), new ConditionSpec(ConditionType.PlcOnline)]);
        var back = ConditionJson.Read(json);
        Assert.Equal(ConditionType.RackTypeMatch, back[0].Type);
        Assert.Equal("B1", back[0].LocationCode);
        Assert.Throws<DomainException>(() => ConditionJson.Read("""[{"type":"DropTable","parameter":"x"}]"""));
        Assert.Throws<DomainException>(() => ConditionJson.Read("""[{"type":"1; DROP TABLE racks"}]"""));
    }
}
