using UnityEngine;

namespace PrismFX
{
    [CreateAssetMenu(menuName = "PrismFX/Highlight Profile", fileName = "PrismHighlightProfile")]
    public class PrismHighlightProfile : ScriptableObject
    {
        public PrismHighlightSettings settings = new PrismHighlightSettings();
    }
}
