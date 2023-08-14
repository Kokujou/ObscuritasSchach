using UnityEngine;
using System.Collections;

public class FigurCollision : MonoBehaviour
{

    // Use this for initialization
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }
    void OnCollisionEnter(Collision col)
    {
        if (gameObject.tag == "MadeTurn" && (col.gameObject.layer == LayerMask.NameToLayer("FigurS") || col.gameObject.layer == LayerMask.NameToLayer("FigurW")))
        {
            col.gameObject.tag = "Destroy";
            //gameObject.transform.localPosition = new Vector3((7 - PlayerPrefs.GetInt("SelPosY")) * 11.25f, gameObject.transform.localPosition.y, (PlayerPrefs.GetInt("SelPosX")) * -11.25f);
            col.rigidbody.velocity *= 100;
            col.rigidbody.angularVelocity *= 100;
        }
    }
    void OnCollisionExit(Collision col)
    {
    }
}
