# MTGA Memory Structure Documentation

## Overview

This document describes the memory structure of Magic: The Gathering Arena (MTGA) and how to navigate it using UnitySpy to extract game data.

## General Architecture

MTGA uses Unity and its Mono runtime. Memory is accessible via UnitySpy which allows reading managed objects directly from the MTGA process.

### Main Entry Point

All access starts with the "Core" Assembly:
```csharp
IAssemblyImage assemblyImage = AssemblyImageFactory.Create(unityProcess, "Core");
```

## Main Object Hierarchy

### WrapperController (Central Entry Point)

The `WrapperController` is the main manager that centralizes access to different services:

```
WrapperController
└── <Instance>k__BackingField (Singleton instance)
    ├── <InventoryManager>k__BackingField
    ├── <AccountClient>k__BackingField
    ├── <CardDatabase>k__BackingField
    └── [other managers...]
```

**Access Path:**
```csharp
assemblyImage["WrapperController"]["<Instance>k__BackingField"]
```

### InventoryManager (Inventory Management)

Manages the player's cards, decks, cosmetics and resources:

```
<InventoryManager>k__BackingField
├── _inventoryServiceWrapper
│   ├── <Cards>k__BackingField (Card collection)
│   ├── _deckDataProvider (Deck management)
│   ├── _cosmeticsProvider
│   ├── m_inventory (Gold/Gems)
│   └── _updates
├── _mercantileServiceWrapper
└── _rotationWarningsSeen
```

**Full Access Path:**
```csharp
assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<InventoryManager>k__BackingField"]["_inventoryServiceWrapper"]
```

### AccountClient (Player Information)

Contains player account information:

```
<AccountClient>k__BackingField
└── <AccountInformation>k__BackingField
    ├── AccountID (string)
    ├── DisplayName (string)
    └── PersonaID (string)
```

**Access Path:**
```csharp
assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<AccountClient>k__BackingField"]["<AccountInformation>k__BackingField"]
```

### CardDatabase (Card Database)

Access to the local SQLite card database:

```
<CardDatabase>k__BackingField
└── <CardDataProvider>k__BackingField
    └── _baseCardDataProvider
        └── _dbConnection
            └── _connectionString (SQLite path)
```

**Access Path:**
```csharp
assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<CardDatabase>k__BackingField"]["<CardDataProvider>k__BackingField"]["_baseCardDataProvider"]["_dbConnection"]["_connectionString"]
```

### PAPA (Player Arena Programming API)

Internal API for events and matches:

```
PAPA
└── _instance
    ├── _eventManager
    │   └── _eventsServiceWrapper
    │       └── _cachedEvents
    │           └── _items (Array of events)
    └── _matchManager
        ├── <MatchID>k__BackingField
        ├── <LocalPlayerInfo>k__BackingField
        └── <OpponentInfo>k__BackingField
```

**Access Path:**
```csharp
assemblyImage["PAPA"]["_instance"]["_eventManager"]["_eventsServiceWrapper"]["_cachedEvents"]["_items"]
```

## Detailed Data Structures

### Card Collection

**Path:** `WrapperController → <Instance>k__BackingField → <InventoryManager>k__BackingField → _inventoryServiceWrapper → <Cards>k__BackingField → _entries`

**Structure:**
- Type: `Array` of `ManagedStructInstance`
- Each element contains:
  - `key` (uint): Card Group ID (grpId)
  - `value` (int): Number owned

**Usage Example:**
```csharp
object[] cards = assemblyImage["WrapperController"]["<Instance>k__BackingField"]["<InventoryManager>k__BackingField"]["_inventoryServiceWrapper"]["<Cards>k__BackingField"]["_entries"];

foreach (var card in cards)
{
    if (card is ManagedStructInstance cardInstance)
    {
        uint grpId = cardInstance.GetValue<uint>("key");
        int owned = cardInstance.GetValue<int>("value");
    }
}
```

### Player Decks

**Path:** `WrapperController → <Instance>k__BackingField → <InventoryManager>k__BackingField → _inventoryServiceWrapper → _deckDataProvider → _allDecks → _entries`

**Structure:**
- Type: `Dictionary` converted to array of entries
- `_count`: Number of decks (e.g., 167)
- `_entries`: Array of `ManagedStructInstance` (197 elements, some empty)
- Each valid entry contains:
  - `key`: Deck GUID (structure with `_a`, `_b`, `_c`, etc.)
  - `value`: Complete deck object
  - `hashCode`: ID hash
  - `next`: Next index (-1 if last)

**GUID Example:**
```
_a: -787409616 (I4)
_b: 14685 (I2) 
_c: 17079 (I2)
_d to _k: individual GUID bytes
```

### Inventory (Gold/Gems)

**Path:** `WrapperController → <Instance>k__BackingField → <InventoryManager>k__BackingField → _inventoryServiceWrapper → m_inventory`

**Structure:**
```csharp
{
    "gems": <number>,
    "gold": <number>
}
```

### Events

**Path:** `PAPA → _instance → _eventManager → _eventsServiceWrapper → _cachedEvents → _items`

**Structure:**
- Type: `Array` of `ManagedClassInstance`
- Each element contains:
  - `InternalEventName` (string): Event ID

### Match State

**Path:** `PAPA → _instance → _matchManager`

**Structure:**
```csharp
{
    "<MatchID>k__BackingField": "match_id",
    "<LocalPlayerInfo>k__BackingField": {
        "MythicPercentile": <float>,
        "MythicPlacement": <int>,
        "RankingClass": <int>,
        "RankingTier": <int>
    },
    "<OpponentInfo>k__BackingField": {
        // Same structure as LocalPlayerInfo
    }
}
```

## Navigation with UnitySpy

### Object Types

1. **IAssemblyImage**: Entry point, contains types
2. **ITypeDefinition**: Class/struct definition
3. **IManagedObjectInstance**: Managed object instance
4. **ManagedStructInstance**: Struct instance (inherits from IManagedObjectInstance)
5. **Array**: .NET array

### Navigation Patterns

#### Static Field Access
```csharp
// For a class with static field
assemblyImage["ClassName"]["StaticFieldName"]
typeDef.GetStaticValue<T>("fieldName")
```

#### Instance Field Access
```csharp
// Via a singleton
assemblyImage["ClassName"]["<Instance>k__BackingField"]["InstanceField"]
managedObject["fieldName"]
```

#### Array Navigation
```csharp
Array array = (Array)object;
for (int i = 0; i < array.Length; i++)
{
    var element = array.GetValue(i);
}
```

#### .NET Dictionary Structures
```csharp
// Dictionary exposed as:
// _entries: Array of key-value entries
// _count: Number of valid elements
// _buckets: Internal hash table

object[] entries = dict["_entries"];
int count = dict.GetValue<int>("_count");
```

## C# Patterns in Unity/MTGA

### Compiler-Generated Fields

- `<FieldName>k__BackingField`: Auto-implemented property backing field
- `<>c__DisplayClass`: Classes generated for closures
- `<Instance>k__BackingField`: Common pattern for singletons

### Unity/Mono Data Types

- `BOOLEAN`: bool
- `I4`: int32
- `I2`: int16  
- `U1`: byte/uint8
- `String`: managed string
- `Object[]`: Object array

## Debugging Tools

### /explore Endpoint

The `/explore` endpoint allows interactive navigation in memory:

**Base URL:** `http://localhost:6842/explore`

**Navigation:** `http://localhost:6842/explore?path=WrapperController|<Instance>k__BackingField|<InventoryManager>k__BackingField`

**Features:**
- Hierarchical navigation with clickable links
- Static/instance field distinction
- Primitive value display
- Memory error handling
- "Back" button to go up

### Useful Exploration Paths

1. **Service root:** `WrapperController|<Instance>k__BackingField`
2. **Cards:** `WrapperController|<Instance>k__BackingField|<InventoryManager>k__BackingField|_inventoryServiceWrapper|<Cards>k__BackingField`
3. **Decks:** `WrapperController|<Instance>k__BackingField|<InventoryManager>k__BackingField|_inventoryServiceWrapper|_deckDataProvider|_allDecks`
4. **Events:** `PAPA|_instance|_eventManager|_eventsServiceWrapper|_cachedEvents|_items`

## Existing API Endpoints

Based on this documentation, the following endpoints are implemented:

- `GET /status` - MTGA process state
- `GET /cards` - Player's card collection  
- `GET /playerId` - Account information
- `GET /inventory` - Gold and gems
- `GET /events` - Available events
- `GET /matchState` - Current match state
- `GET /allcards` - All cards via SQLite
- `GET /explore` - Interactive explorer

## Technical Notes

### GUID Encoding
Unity GUIDs are stored as structures with individual fields:
- `_a`: int32 (4 bytes)
- `_b`, `_c`: int16 (2 bytes each)  
- `_d` to `_k`: byte (1 byte each)

### Memory Management
- UnitySpy uses ReadProcessMemory on Windows
- `/proc/pid/mem` on Linux  
- Direct access to Mono structures in memory
