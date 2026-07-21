using Android.Util;
using Microsoft.Xna.Framework;
using RogueEssence;

namespace PMDO.Android
{
    internal sealed class AndroidGameBase : GameBase
    {
        private LoadPhase lastPhase = (LoadPhase)(-2);

        protected override void Initialize()
        {
            Log.Info("PMDO-ENGINE", "Game.Initialize entered.");
            base.Initialize();
            Log.Info("PMDO-ENGINE", "Game.Initialize completed.");
        }

        protected override void LoadContent()
        {
            base.LoadContent();
            Log.Info("PMDO-ENGINE", "Graphics content host created.");
        }

        protected override void Update(GameTime gameTime)
        {
            if (lastPhase != CurrentPhase)
            {
                lastPhase = CurrentPhase;
                Log.Info("PMDO-ENGINE", "Load phase: " + lastPhase);
            }
            base.Update(gameTime);
        }
    }
}
