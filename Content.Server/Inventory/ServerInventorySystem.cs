using Content.Shared.Explosion;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;

namespace Content.Server.Inventory
{
    public sealed class ServerInventorySystem : InventorySystem
    {
        [Dependency] private readonly SharedHandsSystem _hands = default!; // WD

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<InventoryComponent, BeforeExplodeEvent>(OnExploded);
        }

        private void OnExploded(Entity<InventoryComponent> ent, ref BeforeExplodeEvent args)
        {
            // explode each item in their inventory too
            var slots = new InventorySlotEnumerator(ent);
            while (slots.MoveNext(out var slot))
            {
                if (slot.ContainedEntity != null)
                    args.Contents.Add(slot.ContainedEntity.Value);
            }
        }

        public void TransferEntityInventories(Entity<InventoryComponent?> source, Entity<InventoryComponent?> target)
        {
            if (!Resolve(source.Owner, ref source.Comp) || !Resolve(target.Owner, ref target.Comp))
                return;

            var enumerator = new InventorySlotEnumerator(source.Comp);
            // WD EDIT START
            List<(EntityUid, string)> items = new();
            while (enumerator.NextItem(out var item, out var slot))
            {
                items.Add((item, slot.Name));
            }

            foreach (var (item, name) in items)
            {
                TryUnequip(source, name, true, true, inventory: source.Comp);
                TryEquip(target, item, name, true, true, inventory: target.Comp);
            }
            // WD EDIT END
        }


        // WD edit start - new helper method for drop anything from inventory.
        public void DropAnything(EntityUid uid)
        {
            if (TryGetContainerSlotEnumerator(uid, out var enumerator))
            {
                while (enumerator.MoveNext(out var slot))
                {
                    TryUnequip(uid, slot.ID, true, true);
                }
            }

            foreach (var held in _hands.EnumerateHeld(uid))
            {
                _hands.TryDrop(uid, held);
            }
        }
        // WD edit end
    }
}
