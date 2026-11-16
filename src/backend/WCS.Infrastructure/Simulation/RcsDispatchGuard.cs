using Microsoft.Extensions.Hosting;
using WCS.Application.Abstractions;

namespace WCS.Infrastructure.Simulation;

/// <summary>
/// CR-008A (AUDIT-001). In the Production environment the only RCS adapter that exists is <see cref="RcsSimulator"/>: dispatching a task to it would make the UI and the
/// task list look like real AGV work that never happens. Unless the deployment explicitly opts in (<c>Rcs:AllowSimulatorInProduction=true</c>, for a supervised test),
/// tasks are refused with RCS_NOT_CONNECTED. Development, Testing and Demo behave as before.
/// </summary>
public sealed class RcsDispatchGuard(IRcsClient rcs, IHostEnvironment? environment, bool allowSimulatorInProduction) : IRcsDispatchGuard
{
    public const string NotConnected = "RCS_NOT_CONNECTED";

    public RcsDispatchStatus Check()
    {
        if (environment?.IsProduction() == true && rcs is RcsSimulator && !allowSimulatorInProduction)
            return new RcsDispatchStatus(false, NotConnected,
                "RCS chưa được kết nối: hệ thống Production chưa có RCS adapter thật nên nhiệm vụ không được gửi tới AGV. (Rcs:AllowSimulatorInProduction chỉ dùng cho thử nghiệm có giám sát.)");
        return new RcsDispatchStatus(true, "OK", "RCS dispatch allowed.");
    }
}
