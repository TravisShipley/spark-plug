using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/*
Manual test checklist:
1) Start with active automated generators and note current balances.
2) Apply a 60 second warp and verify balances increase consistently with live/offline math.
3) Apply a warp longer than the passive offline cap and verify the full requested duration is used.
4) Execute a timeWarp trigger/reward action and verify it completes successfully.
5) Confirm no per-cycle trigger spam or replay occurs during the warp.
*/
public sealed class TimeWarpService
{
    private readonly OfflineProgressCalculator offlineProgressCalculator;
    private readonly SaveService saveService;
    private readonly WalletService walletService;

    public TimeWarpService(
        OfflineProgressCalculator offlineProgressCalculator,
        SaveService saveService,
        WalletService walletService
    )
    {
        this.offlineProgressCalculator =
            offlineProgressCalculator
            ?? throw new ArgumentNullException(nameof(offlineProgressCalculator));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        this.walletService =
            walletService ?? throw new ArgumentNullException(nameof(walletService));
    }

    public OfflineSessionResult ApplyWarp(double durationSeconds)
    {
        if (
            double.IsNaN(durationSeconds)
            || double.IsInfinity(durationSeconds)
            || durationSeconds <= 0d
        )
        {
            throw new InvalidOperationException(
                $"TimeWarpService: durationSeconds must be > 0. Found '{durationSeconds}'."
            );
        }

        if (saveService.Data == null)
            throw new InvalidOperationException("TimeWarpService: SaveService.Data is null.");

        Debug.Log(
            $"[TimeWarp] Start {durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s"
        );

        var result = offlineProgressCalculator.Calculate(
            durationSeconds,
            saveService.Data,
            respectOfflineCap: false
        );

        // v1 policy: apply only the net authoritative result. Do not replay per-cycle
        // events or trigger history for each simulated step.
        walletService.ApplyOfflineEarnings(result);

        if (result.HasMeaningfulGain())
            saveService.SaveNow();

        Debug.Log($"[TimeWarp] Complete {result.secondsAway}s {SummarizeGains(result)}");
        return result;
    }

    private static string SummarizeGains(OfflineSessionResult result)
    {
        if (result == null || result.ResourceGains == null || result.ResourceGains.Count == 0)
            return "no gains";

        var summary = new StringBuilder();
        var shown = 0;
        for (int i = 0; i < result.ResourceGains.Count; i++)
        {
            var gain = result.ResourceGains[i];
            if (gain == null || string.IsNullOrWhiteSpace(gain.resourceId))
                continue;

            if (shown > 0)
                summary.Append(", ");

            summary.Append(gain.resourceId);
            summary.Append(" +");
            summary.Append(gain.amount.ToString("0.###", CultureInfo.InvariantCulture));
            shown++;

            if (shown >= 3)
                break;
        }

        if (shown == 0)
            return "no gains";

        if (result.ResourceGains.Count > shown)
            summary.Append(", ...");

        return summary.ToString();
    }
}
