using UnityEngine;

namespace NavalCommander.CardMechanics
{
    public enum CardCategory
    {
        Defense,
        Movement,
        Attack
    }

    [CreateAssetMenu(
        fileName = "New Card Definition",
        menuName = "Naval Commander/Cards/Card Definition")]
    public sealed class CardDefinition : ScriptableObject
    {
        [SerializeField] private string cardId;
        [SerializeField] private CardCategory category;
        [SerializeField] private int damagePerHitCell;

        public string CardId => cardId;
        public CardCategory Category => category;
        public int DamagePerHitCell => damagePerHitCell;
    }
}