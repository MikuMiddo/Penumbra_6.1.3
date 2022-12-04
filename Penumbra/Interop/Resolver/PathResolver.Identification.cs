using System;
using System.Collections;
using System.Linq;
using Dalamud.Data;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Lumina.Excel.GeneratedSheets;
using OtterGui;
using Penumbra.Collections;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Penumbra.Interop.Resolver;

public unsafe partial class PathResolver
{
    // Identify the correct collection for a GameObject by index and name.
    public static ResolveData IdentifyCollection( GameObject* gameObject, bool useCache )
    {
        if( gameObject == null )
        {
            return new ResolveData( Penumbra.CollectionManager.Default );
        }

        try
        {
            if( useCache && IdentifiedCache.TryGetValue( gameObject, out var data ) )
            {
                return data;
            }

            // Login screen. Names are populated after actors are drawn,
            // so it is not possible to fetch names from the ui list.
            // Actors are also not named. So use Yourself > Players > Racial > Default.
            if( !Dalamud.ClientState.IsLoggedIn || Dalamud.GameGui.GetAddonByName( "_CharaMakeTitle", 1 ) != IntPtr.Zero )
            {
                var collection = Penumbra.CollectionManager.ByType( CollectionType.Yourself )
                 ?? CollectionByAttributes( gameObject )
                 ?? Penumbra.CollectionManager.Default;
                return IdentifiedCache.Set( collection, ActorIdentifier.Invalid, gameObject );
            }
            else
            {
                var identifier = Penumbra.Actors.FromObject( gameObject, out var owner, false );
                var collection = CollectionByIdentifier( identifier, out var specialIdentifier )
                 ?? CheckYourself( identifier.Type == IdentifierType.Special ? specialIdentifier : identifier, gameObject )
                 ?? CollectionByAttributes( gameObject )
                 ?? CheckOwnedCollection( identifier, owner )
                 ?? Penumbra.CollectionManager.Default;
                return IdentifiedCache.Set( collection, identifier, gameObject );
            }
        }
        catch( Exception e )
        {
            Penumbra.Log.Error( $"Error identifying collection:\n{e}" );
            return Penumbra.CollectionManager.Default.ToResolveData( gameObject );
        }
    }

    // Get the collection applying to the current player character
    // or the default collection if no player exists.
    public static ModCollection PlayerCollection()
    {
        var gameObject = ( GameObject* )Dalamud.Objects.GetObjectAddress( 0 );
        if( gameObject == null )
        {
            return Penumbra.CollectionManager.ByType( CollectionType.Yourself )
             ?? Penumbra.CollectionManager.Default;
        }

        var player = Penumbra.Actors.GetCurrentPlayer();
        return CollectionByIdentifier( player, out _ )
         ?? CheckYourself( player, gameObject )
         ?? CollectionByAttributes( gameObject )
         ?? Penumbra.CollectionManager.Default;
    }

    // Check both temporary and permanent character collections. Temporary first.
    private static ModCollection? CollectionByIdentifier( ActorIdentifier identifier, out ActorIdentifier specialIdentifier )
        => Penumbra.TempMods.Collections.TryGetCollection( identifier, out var collection, out specialIdentifier )
         || Penumbra.CollectionManager.Individuals.TryGetCollection( identifier, out collection, out specialIdentifier )
                ? collection
                : null;


    // Check for the Yourself collection.
    private static ModCollection? CheckYourself( ActorIdentifier identifier, GameObject* actor )
    {
        if( actor->ObjectIndex                            == 0
        || Cutscenes.GetParentIndex( actor->ObjectIndex ) == 0
        || identifier.Equals( Penumbra.Actors.GetCurrentPlayer() ) )
        {
            return Penumbra.CollectionManager.ByType( CollectionType.Yourself );
        }

        return null;
    }

    // Check special collections given the actor.
    private static ModCollection? CollectionByAttributes( GameObject* actor )
    {
        if( !actor->IsCharacter() )
        {
            return null;
        }

        // Only handle human models.
        var character = ( Character* )actor;
        if( character->ModelCharaId >= 0 && character->ModelCharaId < ValidHumanModels.Count && ValidHumanModels[ character->ModelCharaId ] )
        {
            var race   = ( SubRace )character->CustomizeData[ 4 ];
            var gender = ( Gender )( character->CustomizeData[ 1 ] + 1 );
            var isNpc  = actor->ObjectKind != ( byte )ObjectKind.Player;

            var type       = CollectionTypeExtensions.FromParts( race, gender, isNpc );
            var collection = Penumbra.CollectionManager.ByType( type );
            collection ??= Penumbra.CollectionManager.ByType( CollectionTypeExtensions.FromParts( gender, isNpc ) );
            return collection;
        }

        return null;
    }

    // Get the collection applying to the owner if it is available.
    private static ModCollection? CheckOwnedCollection( ActorIdentifier identifier, GameObject* owner )
    {
        if( identifier.Type != IdentifierType.Owned || !Penumbra.Config.UseOwnerNameForCharacterCollection || owner == null )
        {
            return null;
        }

        var id = Penumbra.Actors.CreateIndividualUnchecked( IdentifierType.Player, identifier.PlayerName, identifier.HomeWorld, ObjectKind.None, uint.MaxValue );
        return CheckYourself( id, owner )
         ?? CollectionByAttributes( owner );
    }

    /// <summary>
    /// Go through all ModelChara rows and return a bitfield of those that resolve to human models.
    /// </summary>
    private static BitArray GetValidHumanModels( DataManager gameData )
    {
        var sheet = gameData.GetExcelSheet< ModelChara >()!;
        var ret   = new BitArray( ( int )sheet.RowCount, false );
        foreach( var (row, idx) in sheet.WithIndex().Where( p => p.Value.Type == ( byte )CharacterBase.ModelType.Human ) )
        {
            ret[ idx ] = true;
        }

        return ret;
    }
}