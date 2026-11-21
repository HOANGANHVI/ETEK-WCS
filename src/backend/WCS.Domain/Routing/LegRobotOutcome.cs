namespace WCS.Domain.Routing;

/// <summary>CR-028: what recording the AMR reported by the RCS did to a leg.</summary>
public enum LegRobotOutcome { NoChange = 0, Assigned = 1, Changed = 2, RefusedRackOnAmr = 3 }
