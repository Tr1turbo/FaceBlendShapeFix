#if FBF_NDMF
using nadena.dev.ndmf;
using Triturbo.FaceBlendShapeFix;
using UnityEngine;

[assembly: ExportsPlugin(typeof(FaceBlendShapeFixPlugin))]
namespace Triturbo.FaceBlendShapeFix
{
    [RunsOnAllPlatforms]
    public class FaceBlendShapeFixPlugin : Plugin<FaceBlendShapeFixPlugin>
    {
        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .Run(FaceBlendShapeFixPass.Instance)
                .PreviewingWith(new FaceBlendShapeFixPreviewFilter()).Then
                .Run("Purge Face BlendShape Fix components", ctx =>
                {
                    foreach (var component in ctx.AvatarRootTransform.GetComponentsInChildren<Runtime.FaceBlendShapeFixComponent>(true))
                    {
                        Object.DestroyImmediate(component);
                    }
                });
        }
    }

   
    
}
#endif