using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hidano.ActivationConstraint.Runtime
{
    /// <summary>Source が複数あるときの合成方法。</summary>
    public enum ActivationSourceCombine
    {
        /// <summary>すべての Source がアクティブなときだけアクティブにする。</summary>
        All,

        /// <summary>いずれかの Source がアクティブならアクティブにする。</summary>
        Any,
    }

    /// <summary>
    /// ParentConstraint と同じ感覚で使える、アクティブ状態の追従コンポーネント。
    /// Source に指定した GameObject の <see cref="GameObject.activeInHierarchy"/> に合わせて
    /// Target（未指定なら自身）の <see cref="GameObject.SetActive"/> を切り替える。
    /// 動作するのは再生中のみで、エディタの編集モードでは何もしない（誤ってシーンを書き換えないため）。
    /// </summary>
    [AddComponentMenu("Hidano/Constraints/Activation Constraint")]
    [DisallowMultipleComponent]
    public sealed class ActivationConstraint : MonoBehaviour
    {
        [SerializeField, Tooltip("オフにすると追従を停止する。ParentConstraint の Is Active 相当。")]
        private bool constraintActive = true;

        [SerializeField, Tooltip("追従元の GameObject。activeInHierarchy を参照する。1 つも設定されていない場合は何もしない。")]
        private List<GameObject> sources = new List<GameObject>();

        [SerializeField, Tooltip("Source が複数あるときの合成方法。")]
        private ActivationSourceCombine combine = ActivationSourceCombine.All;

        [SerializeField, Tooltip("Source のアクティブ状態を反転して適用する。")]
        private bool invert;

        /// <summary>追従の有効／無効。</summary>
        public bool ConstraintActive
        {
            get => constraintActive;
            set => constraintActive = value;
        }

        /// <summary>Source が複数あるときの合成方法。</summary>
        public ActivationSourceCombine Combine
        {
            get => combine;
            set => combine = value;
        }

        /// <summary>アクティブ状態を反転して適用するか。</summary>
        public bool Invert
        {
            get => invert;
            set => invert = value;
        }

        /// <summary>現在の Source 一覧。</summary>
        public IReadOnlyList<GameObject> Sources => sources;

        /// <summary>Source を追加する。</summary>
        public void AddSource(GameObject source)
        {
            if (source == null || sources.Contains(source)) return;
            sources.Add(source);
        }

        /// <summary>Source を取り除く。</summary>
        public void RemoveSource(GameObject source) => sources.Remove(source);

        /// <summary>Source をすべて取り除く。</summary>
        public void ClearSources() => sources.Clear();

        // GameObject が非アクティブになると OnEnable/OnDisable も Update も回らなくなるため、
        // 登録・解除は Awake / OnDestroy で行い、評価は常駐ドライバ側から呼んでもらう。
        private void Awake() => ActivationConstraintDriver.Register(this);

        private void OnDestroy() => ActivationConstraintDriver.Unregister(this);

        /// <summary>Source の状態を Target に反映する。ドライバから毎フレーム呼ばれる。</summary>
        internal void Evaluate()
        {
            if (!enabled || !constraintActive) return;
            if (!TryEvaluateSources(out var active)) return;

            var go = gameObject;
            if (go == null) return;

            if (invert) active = !active;
            if (go.activeSelf != active) go.SetActive(active);
        }

        private bool TryEvaluateSources(out bool active)
        {
            active = false;

            var count = 0;
            var activeCount = 0;
            foreach (var source in sources)
            {
                if (source == null) continue;
                count++;
                if (source.activeInHierarchy) activeCount++;
            }

            // Source 未設定のまま Target を消してしまわないよう、何も適用しない。
            if (count == 0) return false;

            active = combine == ActivationSourceCombine.All ? activeCount == count : activeCount > 0;
            return true;
        }
    }

    /// <summary>
    /// 非アクティブになった GameObject 上の <see cref="ActivationConstraint"/> も評価し続けるための常駐ドライバ。
    /// </summary>
    internal static class ActivationConstraintDriver
    {
        private static readonly List<ActivationConstraint> Constraints = new List<ActivationConstraint>();

        internal static void Register(ActivationConstraint constraint)
        {
            if (constraint == null || Constraints.Contains(constraint)) return;
            Constraints.Add(constraint);
        }

        internal static void Unregister(ActivationConstraint constraint) => Constraints.Remove(constraint);

        // Domain Reload 無効時に前回の再生分が残らないようにする。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            Constraints.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            CollectFromLoadedScenes();
            SceneManager.sceneLoaded += OnSceneLoaded;

            // HideFlags.DontSave を含むのでシーン切り替えでも破棄されない。
            var driver = new GameObject("[ActivationConstraintDriver]") { hideFlags = HideFlags.HideAndDontSave };
            driver.AddComponent<Ticker>();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => CollectFromLoadedScenes();

        // 最初から非アクティブな GameObject では Awake が呼ばれないため、ここで拾い上げる。
        private static void CollectFromLoadedScenes()
        {
            var found = UnityEngine.Object.FindObjectsByType<ActivationConstraint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var constraint in found) Register(constraint);
        }

        private static void Tick()
        {
            for (var i = Constraints.Count - 1; i >= 0; i--)
            {
                var constraint = Constraints[i];
                if (constraint == null)
                {
                    Constraints.RemoveAt(i);
                    continue;
                }

                constraint.Evaluate();
            }
        }

        private sealed class Ticker : MonoBehaviour
        {
            private void LateUpdate() => Tick();
        }
    }
}
