using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class FallResetGuard : MonoBehaviour
{
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float minY = -2f;
    private CharacterController cc;

    void Awake() => cc = GetComponent<CharacterController>();

    void Update()
    {
        if (transform.position.y < minY)
        {
            if (cc != null) cc.enabled = false;
            transform.position = spawnPoint.position;
            if (cc != null) cc.enabled = true;
            // TelemetryLogger.Instance?.LogEvent("FALL_RESET");
            Debug.Log("FALL_RESET");
        }
    }
}