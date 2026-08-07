using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Shared.Core.Misc;

namespace Shared.Ui.BaseDialogs.PackFileTree
{
    public class SearchFilter : NotifyPropertyChangedImpl, IDataErrorInfo
    {
        public string Error { get; set; } = string.Empty;
        public string this[string columnName] => _filterError;

        private readonly ObservableCollection<TreeNode> _nodeCollection;
        private Dictionary<TreeNode, bool>? _searchExpansionState;
        private string _filterError = string.Empty;

        string _filterText = "";
        public string FilterText
        {
            get => _filterText;
            set
            {
                SetAndNotify(ref _filterText, value);
                _filterError = Filter(_filterText);
            }
        }

        private bool _showFoldersOnly;
        public bool ShowFoldersOnly
        {
            get => _showFoldersOnly;
            set
            {
                SetAndNotify(ref _showFoldersOnly, value);
                _filterError = Filter(FilterText);
            }
        }

        List<string>? _extensionFilter;
        public int AutoExapandResultsAfterLimitedCount { get; set; } = 25;

        public SearchFilter(ObservableCollection<TreeNode> nodes)
        {
            _nodeCollection = nodes;
        }

        string Filter(string text)
        {
            Regex expression;
            try
            {
                expression = new Regex(
                    text,
                    RegexOptions.Compiled |
                    RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant);
            }
            catch (Exception e)
            {
                return e.Message;
            }

            var hasSearchText = string.IsNullOrEmpty(text) == false;
            if (hasSearchText)
            {
                if (_searchExpansionState == null)
                    _searchExpansionState = CaptureExpansionState();
                else
                    RestoreExpansionState(_searchExpansionState);
            }
            else if (_searchExpansionState != null)
            {
                RestoreExpansionState(_searchExpansionState);
                _searchExpansionState = null;
            }

            var matches = new List<TreeNode>();
            foreach (var item in _nodeCollection)
                ApplyVisibility(item, expression, hasSearchText, matches);

            if (hasSearchText &&
                AutoExapandResultsAfterLimitedCount != -1 &&
                matches.Count <= AutoExapandResultsAfterLimitedCount)
            {
                ExpandMatchPaths(matches);
            }

            return "";
        }

        private bool ApplyVisibility(
            TreeNode node,
            Regex expression,
            bool hasSearchText,
            List<TreeNode> matches)
        {
            if (node.NodeType == NodeType.File)
            {
                var isMatch =
                    HasValidExtension(node.Name) &&
                    expression.IsMatch(node.Name);
                node.IsVisible = ShowFoldersOnly == false && isMatch;
                if (isMatch)
                    matches.Add(node);
                return isMatch;
            }

            var hasChildMatch = false;
            foreach (var child in node.Children)
            {
                if (ApplyVisibility(
                    child,
                    expression,
                    hasSearchText,
                    matches))
                {
                    hasChildMatch = true;
                }
            }

            var isFolderMatch =
                ShowFoldersOnly &&
                hasSearchText &&
                expression.IsMatch(node.Name);
            if (isFolderMatch)
                matches.Add(node);

            var isVisible = node.Children.Count == 0 &&
                node.NodeType == NodeType.Root
                ? true
                : ShowFoldersOnly
                    ? hasSearchText == false ||
                        isFolderMatch ||
                        hasChildMatch
                    : hasChildMatch;
            node.IsVisible = isVisible;
            return isVisible;
        }

        private bool HasValidExtension(string fileName)
        {
            if (_extensionFilter == null)
                return true;

            foreach (var extension in _extensionFilter)
            {
                if (fileName.Contains(
                    extension,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<TreeNode, bool> CaptureExpansionState()
        {
            var state = new Dictionary<TreeNode, bool>();
            foreach (var root in _nodeCollection)
            {
                root.ForeachNode(node =>
                    state[node] = node.IsNodeExpanded);
            }

            return state;
        }

        private static void RestoreExpansionState(
            IReadOnlyDictionary<TreeNode, bool> state)
        {
            foreach (var (node, isExpanded) in state)
                node.IsNodeExpanded = isExpanded;
        }

        private static void ExpandMatchPaths(
            IReadOnlyCollection<TreeNode> matches)
        {
            foreach (var match in matches)
            {
                var current = match.NodeType == NodeType.File
                    ? match.Parent
                    : match;
                while (current != null)
                {
                    if (current.IsVisible)
                        current.IsNodeExpanded = true;
                    current = current.Parent;
                }
            }
        }

        public void SetExtensions(List<string> extentions)
        {
            _extensionFilter = extentions;
            _filterError = Filter(FilterText);
        }

        public void Refresh() => _filterError = Filter(FilterText);
    }
}
