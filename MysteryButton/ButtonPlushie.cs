namespace MysteryButton;

public class ButtonPlushie : GrabbableObject
{
    private ButtonAI ai;

    private bool hasBeenUsed;
    
    public override void Start()
    {
        hasBeenUsed = false;
        ai = gameObject.AddComponent<ButtonAI>();
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