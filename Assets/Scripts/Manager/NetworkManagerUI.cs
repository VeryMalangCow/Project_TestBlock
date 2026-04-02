using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Threading.Tasks;

namespace ProjectBlockTest.Network
{
    public class NetworkManagerUI : MonoBehaviour
    {
        [Header("### Buttons")]
        [SerializeField] private Button hostBtn;
        [SerializeField] private Button clientBtn;

        [Header("### UI Elements")]
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private TextMeshProUGUI statusText;

        private async void Awake()
        {
            try
            {
                await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log($"[NetworkManagerUI] Signed in as: {AuthenticationService.Instance.PlayerId}");
                }

                if (statusText != null) statusText.text = "Ready to connect (Relay Mode)";
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkManagerUI] Init Error: {e.Message}");
                if (statusText != null) statusText.text = "Relay Initialization Failed.";
            }

            hostBtn.onClick.AddListener(StartRelayHost);
            clientBtn.onClick.AddListener(StartRelayClient);
        }

        private async void StartRelayHost()
        {
            if (NetworkManager.Singleton == null) return;

            try
            {
                if (statusText != null) statusText.text = "Creating Relay Room...";
                
                // [FIX] 네임스페이스 명시적 지정 (Unity.Services.Relay.Models.Allocation)
                Unity.Services.Relay.Models.Allocation allocation = await Unity.Services.Relay.RelayService.Instance.CreateAllocationAsync(3);
                
                string joinCode = await Unity.Services.Relay.RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                
                Debug.Log($"[Relay] Host Join Code: {joinCode}");
                if (joinCodeInput != null) joinCodeInput.text = joinCode;
                if (statusText != null) statusText.text = $"Host Started! Code: {joinCode}";

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetRelayServerData(
                        allocation.RelayServer.IpV4,
                        (ushort)allocation.RelayServer.Port,
                        allocation.AllocationIdBytes,
                        allocation.Key,
                        allocation.ConnectionData
                    );
                    
                    NetworkManager.Singleton.StartHost();
                }
            }
            catch (Unity.Services.Relay.RelayServiceException e)
            {
                Debug.LogError($"[Relay] Host Exception: {e.Message}");
                if (statusText != null) statusText.text = "Failed to create Relay.";
            }
        }

        private async void StartRelayClient()
        {
            if (NetworkManager.Singleton == null) return;

            try
            {
                string joinCode = joinCodeInput != null ? joinCodeInput.text : "";
                if (string.IsNullOrEmpty(joinCode) || joinCode.Length != 6)
                {
                    if (statusText != null) statusText.text = "Invalid Join Code (6 digits required)";
                    return;
                }

                if (statusText != null) statusText.text = $"Joining {joinCode}...";

                // [FIX] 네임스페이스 명시적 지정 (Unity.Services.Relay.Models.JoinAllocation)
                Unity.Services.Relay.Models.JoinAllocation joinAllocation = await Unity.Services.Relay.RelayService.Instance.JoinAllocationAsync(joinCode);

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetRelayServerData(
                        joinAllocation.RelayServer.IpV4,
                        (ushort)joinAllocation.RelayServer.Port,
                        joinAllocation.AllocationIdBytes,
                        joinAllocation.Key,
                        joinAllocation.ConnectionData,
                        joinAllocation.HostConnectionData
                    );

                    NetworkManager.Singleton.StartClient();
                    if (statusText != null) statusText.text = "Connected to Relay!";
                }
            }
            catch (Unity.Services.Relay.RelayServiceException e)
            {
                Debug.LogError($"[Relay] Client Exception: {e.Message}");
                if (statusText != null) statusText.text = "Relay Join Failed.";
            }
        }
    }
}
