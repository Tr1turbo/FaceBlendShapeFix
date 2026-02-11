#if FBF_NDMF
using nadena.dev.ndmf;


namespace Triturbo.FaceBlendShapeFix
{
    public class FaceBlendShapeFixPass : Pass<FaceBlendShapeFixPass>
    {
        public override string DisplayName => "Face BlendShape Fix";

        protected override void Execute(BuildContext context)
        {
            var component = context.AvatarRootTransform.GetComponentInChildren<Runtime.FaceBlendShapeFixComponent>();
            if(component  == null) return;


            if (component.TargetRenderer != null)
            {
                component.TargetRenderer.sharedMesh = MeshBlendShapeProcessor.BakeCorrectedShapes(component);
            }


        }
    }
}
#endif