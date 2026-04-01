using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectBlockTest.Network
{
    public class NetworkManagerUI : MonoBehaviour
    {
        [Header("### Buttons")]
        [SerializeField] private Button hostBtn;
        [SerializeField] private Button serverBtn;
        [SerializeField] private Button clientBtn;

        [Header("### Settings")]
        [SerializeField] private TMP_InputField ipInput;
        [SerializeField] private string defaultIp = "127.0.0.1";

        private void Awake()
        {
            // Initialize InputField
            if (ipInput != null)
            {
                ipInput.text = defaultIp;
            }

            hostBtn.onClick.AddListener(() =>
            {
                UpdateTransportAddress();
                NetworkManager.Singleton.StartHost();
            });

            serverBtn.onClick.AddListener(() =>
            {
                UpdateTransportAddress();
                NetworkManager.Singleton.StartServer();
            });

            clientBtn.onClick.AddListener(() =>
            {
                UpdateTransportAddress();
                NetworkManager.Singleton.StartClient();
            });
        }

        private void UpdateTransportAddress()
        {
            if (ipInput == null) return;

            // Get the UnityTransport component from NetworkManager
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                string targetIp = string.IsNullOrWhiteSpace(ipInput.text) ? defaultIp : ipInput.text;
                transport.ConnectionData.Address = targetIp;
                Debug.Log($"[NetworkManagerUI] Set Connection Address to: {targetIp}");
            }
        }
    }
}
