using UnityEngine;

public class GetPizza : MonoBehaviour
{
    GameObject pizza;
    Vector3 pizzaEnd;
    ParticleSystem confetti;

    Transform playerTransform;

    void Start()
    {
        pizza = transform.Find("Pizza").gameObject;
        confetti = transform.Find("confetti").GetComponent<ParticleSystem>();
        playerTransform = GameObject.FindWithTag("MainCamera").transform;

        pizzaEnd = pizza.transform.position;

        // hide the pizza at the start
        pizza.SetActive(false);
    }


    public void GivePizza()
    {
        pizza.transform.position = playerTransform.position + Vector3.down*0.5f; // place the pizza slightly below the player's eyeline
        pizza.transform.SetParent(transform);
        pizza.SetActive(true);

        // move the pizza from the player's position to this transform's position over 2 second
        StartCoroutine(MovePizza());

        var main = confetti.main;
        Gradient rainbow = new Gradient();
        rainbow.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.red, 0f),
                new GradientColorKey(Color.yellow, 0.25f),
                new GradientColorKey(Color.green, 0.5f),
                new GradientColorKey(Color.blue, 0.75f),
                new GradientColorKey(Color.magenta, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        main.startColor = new ParticleSystem.MinMaxGradient(rainbow);
        confetti.Play();
    }
    

    private System.Collections.IEnumerator MovePizza()
    {
        Vector3 startPos = pizza.transform.position;
        Vector3 endPos = pizzaEnd;
        float duration = 2f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            pizza.transform.position = Vector3.Lerp(startPos, endPos, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        pizza.transform.position = endPos;
    }
}
