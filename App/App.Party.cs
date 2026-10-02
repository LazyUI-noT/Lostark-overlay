using GaugeOverlay.App.Services;
using GaugeOverlay.App.UI;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Net;

namespace GaugeOverlay.App;

/// <summary>Who gets into a party hosted here (참가 요청 승인, 신뢰한 기기, 내보내기): the requests as popups, the trusted
/// devices kept in the settings, the kicked ones listed in 설정 → 파티, and refused joins as tray notices.</summary>
public partial class App
{
    void WireAdmission(PartyService party, Settings settings, Action save)
    {
        IEnumerable<string> Trusted() => settings.Party.TrustedDevices.Select(t => t.Device);
        party.SetAdmission(settings.Party.ApproveJoins, Trusted());
        _settingsWindow.SettingsChanged += () => party.SetAdmission(settings.Party.ApproveJoins, Trusted());
        _settingsWindow.UnblockRequested += device =>
        {
            party.Unblock(device);
            _settingsWindow.SetPartyBlocked(party.Blocked);
        };
        party.StatusChanged += status => Dispatcher.BeginInvoke(() =>
        {
            if (!party.IsHost) JoinRequestPopup.CloseAll();   // a request outlives no session
            _settingsWindow.SetPartyBlocked(party.Blocked);
        });
        party.Admission += changes => Dispatcher.BeginInvoke(() =>
        {
            foreach (var request in party.IsHost ? changes.Asked : [])
                JoinRequestPopup.Ask(request, ok =>
                {
                    if (!(ok ? party.Approve(request.Id) : party.Decline(request.Id)))
                        _tray.Notify($"{request.Name}님의 참가 요청은 이미 끝났습니다.");
                });
            foreach (var id in changes.Withdrawn) JoinRequestPopup.Withdraw(id);
            var trusted = settings.Party.TrustedDevices;
            foreach (var device in changes.Trusted)
            {
                trusted.RemoveAll(t => t.Device == device.Device);
                trusted.Add(device);
            }
            foreach (var device in changes.Untrusted) trusted.RemoveAll(t => t.Device == device);
            if (changes.Trusted.Count + changes.Untrusted.Count > 0) save();
            foreach (var line in changes.Refused) _tray.Notify(line);
            _settingsWindow.SetPartyBlocked(party.Blocked);
        });
    }
}
