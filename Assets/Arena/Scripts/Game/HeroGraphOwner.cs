using UnityEngine;
using UnityEngine.Playables;

namespace Arena.Game
{
    /// <summary>Destroys a hero's animation graph together with its model.</summary>
    public class HeroGraphOwner : MonoBehaviour
    {
        public PlayableGraph Graph;
        void OnDestroy() { if (Graph.IsValid()) Graph.Destroy(); }
    }
}
