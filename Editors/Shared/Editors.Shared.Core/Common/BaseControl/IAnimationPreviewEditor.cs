using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;

namespace Editors.Shared.Core.Common.BaseControl
{
    public interface IAnimationPreviewEditor
    {
        void PreviewAnimation(AnimationFile animation, AnimationFile skeleton, string animationPath, PackFile? metadata, PackFile? persistentMetadata = null);
    }
}
