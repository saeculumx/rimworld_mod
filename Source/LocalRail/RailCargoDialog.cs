using UnityEngine;
using Verse;
namespace LocalRail;

public class Dialog_RailCargo : Window
{
    private readonly Building_RailTrain train;
    public override Vector2 InitialSize=>new(560,460);
    public Dialog_RailCargo(Building_RailTrain train)
    {this.train=train;doCloseX=true;preventCameraMotion=false;absorbInputAroundWindow=false;draggable=true;}
    public override void DoWindowContents(Rect rect)
    {
        if(!train.Spawned){Close();return;}
        Text.Font=GameFont.Medium;Widgets.Label(new(0,0,rect.width,35),"LR_CargoSlots".Translate());Text.Font=GameFont.Small;
        Widgets.Label(new(0,43,rect.width,50),"LR_CargoSlotsHelp".Translate());
        var shipments=train.Map.GetComponent<RailMapComponent>().Freight.Where(f=>f.train==train).ToList();
        for(int i=0;i<RailMapComponent.FreightCapacity;i++)
        {
            var box=new Rect(i%4*130,105+i/4*96,120,87);Widgets.DrawBox(box);
            Widgets.Label(new(box.x+4,box.y+3,25,22),(i+1).ToString());
            if(i>=shipments.Count)continue;
            var shipment=shipments[i];var item=shipment.contents.FirstOrDefault();if(item==null)continue;
            Widgets.ThingIcon(new(box.x+38,box.y+5,45,45),item);
            Widgets.Label(new(box.x+5,box.y+49,110,28),item.stackCount+" × "+item.LabelNoCount);
            TooltipHandler.TipRegion(box,item.LabelCap+"\n→ "+shipment.destination?.PlatformLabel(shipment.destinationPlatform));
        }
    }
}
