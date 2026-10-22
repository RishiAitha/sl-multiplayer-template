using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class RaceCar : NetworkBehaviour
{
    [SerializeField] private Sprite[] sprites;
    [SerializeField] private int finishProgress = 8;
    [SerializeField] private float laneSpacing = 1.5f;
    [SerializeField] private float stepDistance = 0.75f;

    private readonly NetworkVariable<int> progress = new NetworkVariable<int>();
    private readonly NetworkVariable<int> lane = new NetworkVariable<int>();
    private readonly NetworkVariable<bool> won = new NetworkVariable<bool>();
    private SpriteRenderer spriteRenderer;

    public override void OnNetworkSpawn()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (IsServer)
        {
            var clients = NetworkManager.Singleton.ConnectedClientsIds;
            for (int i = 0; i < clients.Count; i++)
                if (clients[i] == OwnerClientId) lane.Value = i;
        }
        lane.OnValueChanged += OnLaneChanged;
        progress.OnValueChanged += OnProgressChanged;
        won.OnValueChanged += OnWonChanged;
        UpdateCar();
        if (won.Value) ShowResult();
    }

    public override void OnNetworkDespawn()
    {
        lane.OnValueChanged -= OnLaneChanged;
        progress.OnValueChanged -= OnProgressChanged;
        won.OnValueChanged -= OnWonChanged;
    }

    private void Update()
    {
        if (IsOwner && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
            (UnityEngine.EventSystems.EventSystem.current == null ||
             !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
            AdvanceRpc();
    }

    [Rpc(SendTo.Server)]
    private void AdvanceRpc()
    {
        if (progress.Value >= finishProgress) return;
        foreach (var car in FindObjectsByType<RaceCar>(FindObjectsSortMode.None))
            if (car.won.Value) return;
        progress.Value++;
        if (progress.Value >= finishProgress) won.Value = true;
    }

    private void OnLaneChanged(int previous, int current) => UpdateCar();
    private void OnProgressChanged(int previous, int current) => UpdateCar();
    private void OnWonChanged(bool previous, bool current)
    {
        if (current) ShowResult();
    }

    private void UpdateCar()
    {
        if (spriteRenderer == null) return;
        if (sprites.Length > 0) spriteRenderer.sprite = sprites[lane.Value % sprites.Length];
        transform.position = new Vector3((lane.Value - 1) * laneSpacing, -3f + progress.Value * stepDistance, 0f);
    }

    private void ShowResult()
    {
        FindFirstObjectByType<SessionMenu>().ShowResult(IsOwner);
    }
}
