using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 10f, -10f);

    void LateUpdate()
    {
        transform.position = target.position + offset;
    }
}