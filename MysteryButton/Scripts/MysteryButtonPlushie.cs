namespace MysteryButton;

public class MysteryButtonPlushie : GrabbableObject
{
    private MysteryButtonAI ai;

    private bool hasBeenUsed;
    
    public override void Start()
    {
        hasBeenUsed = false;
        ai = gameObject.AddComponent<MysteryButtonAI>();
        base.Start();
    }
    
    public override void ItemActivate(bool used, bool buttonDown = true) {
        if (!hasBeenUsed)
        {
            hasBeenUsed = true;
            ai.DoEffect(playerHeldBy.name);
        }
    }
}