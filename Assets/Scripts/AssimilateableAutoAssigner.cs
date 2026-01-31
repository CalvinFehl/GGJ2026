using UnityEngine;
using UnityEngine.SceneManagement;

public static class AssimilateableAutoAssigner
{
    private const string AssimilateableTag = "Assimilateable";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject target = transforms[i].gameObject;
                if (!ShouldAssignAssimilateable(target))
                {
                    continue;
                }

                if (!target.TryGetComponent(out Assimilateable _))
                {
                    target.AddComponent<Assimilateable>();
                }

                if (target.tag != AssimilateableTag)
                {
                    target.tag = AssimilateableTag;
                }
            }
        }
    }

    private static bool ShouldAssignAssimilateable(GameObject target)
    {
        if (target.CompareTag(AssimilateableTag))
        {
            return true;
        }

        return target.CompareTag("Untagged");
    }
}
