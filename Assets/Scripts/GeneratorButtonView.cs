using System;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

public class GeneratorButtonView : MonoBehaviour
{
    [SerializeField]
    private Button runButton;

    [SerializeField]
    private string primaryResourceId = "currencySoft";

    [SerializeField]
    private string critResourceId = "currencyHard";

    [SerializeField]
    private double cashGenerated;

    [SerializeField]
    private double goldGenerated;

    [SerializeField]
    private float critChance;

    private void Start()
    {
        // Bind button click event to publish increment event
        runButton
            .onClick.AsObservable()
            .Subscribe(_ =>
            {
                float random = UnityEngine.Random.Range(0f, 1f);
                if (random < this.critChance)
                {
                    double randomGold = Math.Round(
                        this.goldGenerated * UnityEngine.Random.Range(0f, 1f)
                    );
                    EventSystem.OnIncrementBalance.OnNext((critResourceId, randomGold));
                }

                EventSystem.OnIncrementBalance.OnNext((primaryResourceId, this.cashGenerated));
            })
            .AddTo(this);
    }
}
