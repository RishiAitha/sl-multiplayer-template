using System;
using System.Threading;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

public class MultiplayerManager : MonoBehaviour
{
    private ISession session;
    [SerializeField] private string queueName = "RaceQueue";
    [SerializeField] private int maxPlayers = 3;
    private bool busy;

    public string Status { get; private set; } = "Initializing...";
    public event Action<string> StatusChanged;
    public event Action SessionJoined;

    async void Start()
    {
        SetStatus("Initializing...");
        try
        {
            await UnityServices.InitializeAsync();
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            SetStatus("Ready");
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
        }
    }

    public async void StartMatchmaking()
    {
        if (busy || !AuthenticationService.Instance.IsSignedIn) return;
        busy = true;
        SetStatus("Finding players...");
        try
        {
            if (session != null) await session.LeaveAsync();
            session = await MultiplayerService.Instance.MatchmakeSessionAsync(
                new MatchmakerOptions { QueueName = queueName },
                new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork(),
                CancellationToken.None);
            SessionJoined?.Invoke();
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
        }
        finally
        {
            busy = false;
        }
    }

    public async void Leave()
    {
        if (busy || session == null) return;
        busy = true;
        try
        {
            await session.LeaveAsync();
            session = null;
            SetStatus("Ready");
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
        }
        finally
        {
            busy = false;
        }
    }

    private void SetStatus(string value)
    {
        Status = value;
        StatusChanged?.Invoke(value);
    }
}
