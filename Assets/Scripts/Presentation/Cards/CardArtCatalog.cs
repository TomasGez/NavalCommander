using System;
using System.Collections.Generic;
using UnityEngine;

namespace NavalCommander.Presentation.Cards
{
    /// <summary>
    /// Asocia cada id de carta (CardDefinition.CardId) con su arte. Vive en Assets/Resources/CardArt
    /// para cargarse en runtime con Resources.Load; los sprites se referencian desde Assets/Cards sin moverlos.
    /// Un id sin entrada devuelve false y la interfaz muestra el id como texto.
    /// </summary>
    [CreateAssetMenu(fileName = "CardArtCatalog", menuName = "Naval Commander/Cards/Card Art Catalog")]
    public sealed class CardArtCatalog : ScriptableObject
    {
        public const string ResourcePath = "CardArt/CardArtCatalog";

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<string, Sprite> _byCardId;

        public bool TryGetSprite(string cardId, out Sprite sprite)
        {
            if (_byCardId == null) Build();
            return _byCardId.TryGetValue(cardId ?? string.Empty, out sprite) && sprite != null;
        }

        private void Build()
        {
            _byCardId = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (Entry entry in entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.CardId)) _byCardId[entry.CardId] = entry.Sprite;
            }
        }

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string cardId;
            [SerializeField] private Sprite sprite;

            public string CardId => cardId;
            public Sprite Sprite => sprite;
        }
    }
}
