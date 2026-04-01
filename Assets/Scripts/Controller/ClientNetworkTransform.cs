using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Used for syncing a transform from client to server. 
/// This allows the player to feel immediate movement without server round-trip delay.
/// </summary>
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    /// <summary>
    /// Used to determine who can write to this transform. Owner client in this case.
    /// </summary>
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
