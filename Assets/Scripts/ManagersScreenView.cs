using UnityEngine;

public sealed class ManagersScreenView : UiScreenView
{
    [Header("UI")]
    [SerializeField]
    private Transform listContainer;

    [SerializeField]
    private UpgradeEntryView entryPrefab;

    private ManagersScreenViewModel viewModel;

    public override void OnBeforeShow(object payload)
    {
        ClearList();

        if (entryPrefab == null || listContainer == null)
        {
            Debug.LogError(
                "ManagersScreenView: entryPrefab or listContainer is not assigned.",
                this
            );
            return;
        }

        viewModel = payload as ManagersScreenViewModel;
        if (viewModel == null && Manager != null)
            viewModel = Manager.ManagersScreenViewModel;

        if (viewModel == null)
        {
            Debug.LogError(
                "ManagersScreenView: ManagersScreenViewModel is not available for binding.",
                this
            );
            return;
        }

        for (int i = 0; i < viewModel.Entries.Count; i++)
        {
            var entryViewModel = viewModel.Entries[i];
            if (entryViewModel == null)
                continue;

            var entry = Instantiate(entryPrefab, listContainer);
            entry.name = $"Upgrade_{entryViewModel.UpgradeId}";
            entry.Bind(entryViewModel);
        }
    }

    private void ClearList()
    {
        if (listContainer == null)
            return;

        for (int i = listContainer.childCount - 1; i >= 0; i--)
        {
            var child = listContainer.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }
    }
}
