# Kerbal Relations

Happiness, relationships and families for Kerbal Space Program 1.12.

Kerbals have a mood. Kerbals who spend time together get to know each other, some become couples, and
couples can lay eggs that hatch into children who grow up to join the crew. Kerbals have an age and a
birthplace, earn ribbons for what they do, and Mission Control offers missions about them. A
relationship graph shows who knows whom.

Works on its own, and together with [USI Life Support](https://github.com/UmbraSpaceIndustries/USI-LS)
and [MKS](https://github.com/UmbraSpaceIndustries/MKS), where it replaces the Kolony Growth timer.

## Requirements

- KSP 1.12.x
- [HarmonyKSP](https://github.com/KSPModdingLibs/HarmonyKSP) (`GameData/000_Harmony`)
- Optional: USI-LS / MKS

## How it works

### Happiness (0 to 200 %)

| Situation | Happiness |
| --- | --- |
| Nothing going on, or at the KSC | 70 % |
| Alone for more than 10 days | 50 % |
| With at least one other Kerbal | 100 % |
| With a Kerbal they have a 100 % bond with | 150 % |

Mood follows these levels over a few days. On top come temporary bonuses and penalties: reaching a new
sphere of influence, landing (more the first time on each body), coming home, a promotion, falling in
love, a break-up, an egg, a hatching, missing an absent partner, losing a friend, and random events such
as "Had a fight with Bill over which planet looks nicest" (-5 %).

Happiness scales, by default from x0.5 at 0 % through x1 at 100 % to x2 at 200 %:

- movement speed on EVA
- science produced by scientists in a lab
- experience gained

A lone Kerbal at the default 70 % therefore works at x0.85.

### Relationships (0 to 100 %)

Kerbals on the same vessel, or on landed vessels within 2 km of each other, are together. Being together
creates a relationship at 0 % that grows by about 0.25 % per day; every pair has its own fixed
compatibility, so some bonds grow faster. Only aboard the same vessel can a bond grow all the way:
between vessels standing side by side it stops at 60 %, and the limit sinks to 20 % for vessels 2 km
apart. A bond that is already past the limit keeps what it has. After 30 days apart a bond slowly fades.
Nothing changes at the KSC.

An opposite-sex pair of single adults becomes a couple at 70 % and breaks up below 50 %.

### Families

Switch on in Difficulty Settings (on by default). A couple at 85 % or more who are together can lay an
egg if there is a free seat in a nest:

- with USI-LS: a part whose **Kolony Growth** toggle is switched on (it is off by default). The mod
  adds the toggle to every habitation part with seats, from any mod, next to the MKS Tundra Colonization
  Modules and Atlas Kerbitats that already have it. Inflatable habitats must be deployed. The USI-LS
  timer that spawns a random adult is switched off.
- without USI-LS: any part with a free seat.

The egg takes up a seat. It hatches after 9 months into a child, who becomes an adult with a profession
after 14 years. Children are small, have big eyes and skin of their own colour, teal for boys and pink for girls, that turns green when they grow up. Eggs and children are passengers: they cannot fly a
vessel or work, but they can go outside. A child walks around like any Kerbal, only smaller. An egg
rolls: steer it with the walking keys, hop with the jump key, and press the boarding key near a hatch to
put it back aboard. Nothing breaks an egg that is outside. They count as crew for life support. Couples wait a year between eggs.

This works on vessels you are not flying as well: the egg is added to the saved vessel. Hover over a
couple in the relationship window to see what, if anything, is keeping them from having an egg.

### Age and birthplace

Every Kerbal has an age, shown when you hover over them in the relationship window. Hired Kerbals come
with an age between 18 and 45 and were born on the home world. Kerbals who hatched in your space
programme were born where their egg hatched ("Born on Minmus, aboard Hab One"), are children until 14
and age from there. The growing-up speed in the difficulty settings makes children age faster, so a
child always comes of age at 14.

### Ribbons

Kerbals earn ribbons, each with a small mood boost:

- one for every other world they travel to, and another for landing there
- friendship: a first friend (50 % bond), best friends (100 %), knowing ten Kerbals
- love: becoming a couple, a break-up, five years together, meeting again after more than a year apart
- family: becoming a parent, three children, raising a child to adulthood, a grandchild
- birth: hatched away from the home world, the first Kerbal ever born at a body, coming of age
- travel: three worlds visited, five worlds landed on, coming home after more than a year, five years
  away in one go
- life: a hundred days alone, 180 % happiness, 20 % happiness, losing someone close, level 3, level 5,
  turning fifty
- missions: keeping a lonely Kerbal company, chaperoning a school class

Every ribbon is a small medal: a strip of striped cloth with a badge that shows a picture of what it is
for. Click a Kerbal in the relationship window to see their medals under the graph; hover over one to
read what it is for. On a save that was started before ribbons existed, what the Kerbals had already
done is caught up on quietly the first time.

### Missions

Career games only; switch off in the difficulty settings. Mission Control offers missions about your
Kerbals when the situation calls for one:

| Mission | Offered when | Done when |
| --- | --- | --- |
| Keep a Kerbal company | a Kerbal is lonely | they have had company for 20 days, or are home |
| Reunite a couple | a couple has been apart for 30 days | the two are in the same place |
| Bring a Kerbal home | a Kerbal has been away for 2 years | they are back at the KSC |
| Keep a crew happy | you have a crew away from home | 3 to 5 Kerbals stay at 110 to 130 % for 60 to 400 days |
| First child of a body | Kerbals have orbited a body where nobody was born yet | an egg hatches there |
| A family of four | there is a couple | two parents and two of their children live together away from home |
| Show a school class a body | Kerbals have orbited that body | the class has been there and is home again |

Missions only show up when you can build what they need: a crewed part for all of them, a part with two
seats for school trips, and a part eggs can be laid in for the two family missions. They pay funds and
reputation, and the Kerbals they were about are proud for a while.

A school class is two to four children, sometimes with the class egg. They are tourists: they need a
seat, cannot go on EVA and leave when the trip is over. They look and count as children and eggs, make
friends on the way, but do not grow up or collect ribbons.

### The relationship window

Toolbar button (a heart) in flight, the tracking station and the space center. Kerbals with stronger
bonds sit closer together; Kerbals who never met are not connected. The ring around a portrait shows
happiness. Hover over a Kerbal or a line for details, including which vessel a Kerbal is aboard and
where that is; drag Kerbals to rearrange; click a Kerbal to see their ribbons; right-click a Kerbal to
go to their vessel.

The button at the top chooses who is shown: everyone, the vessel you are flying, the KSC, or one body.
Every choice says how many Kerbals are there, and only places with Kerbals are listed.

## Settings

Per save, under **Difficulty Settings > Kerbal Relations**: enable/disable the mod, families, random
events, missions and screen messages; relationship speed; growing-up speed (divides the 9 months and 14
years); event frequency.

All numbers are in `GameData/KerbalRelations/Config/*.cfg` and can be changed with ModuleManager patches.
For test buttons in the relationship window, set `Debug = true` with a patch in a file of your own
outside the mod's folder, so that updating the mod does not reset it:

```
@KR_SETTINGS
{
	@Debug = true
}
```

## For other mods

Declare how your own values react to happiness in a config file:

```
KR_EFFECT
{
	name = MyDrillSpeed
	key = 0 0.8
	key = 100 1
	key = 200 1.3
}
```

and ask for the multiplier, with or without a reference to `KerbalRelations.dll`:

```csharp
double m = KerbalRelations.KerbalRelationsAPI.GetMultiplier(kerbal.name, "MyDrillSpeed");
double crew = KerbalRelations.KerbalRelationsAPI.GetCrewMultiplier(part, "MyDrillSpeed");
```

`KerbalRelationsAPI` also offers `GetHappiness`, `AddModifier`, `GetRelationship`, `GetPartner`,
`GetLifeStage`, `GetAge`, `GetBirthBody`, `HasRibbon`, `AwardRibbon`, `IsActive` and the
`OnHappinessChanged` event. All return neutral values (100 %, x1) when the mod is switched off or the
Kerbal is unknown.

Mods can add their own mood events (`KR_EVENT`), modifiers (`KR_MODIFIER`) and ribbons (`KR_RIBBON`, handed
out with `AwardRibbon`, with one of the built-in pictures or a texture of their own); see the config
files for the format.

## Building

```
cd Source
dotnet test                                              # simulation tests, no KSP needed
dotnet build KerbalRelations/KerbalRelations.csproj      # needs a KSP install
dotnet build KerbalRelations/KerbalRelations.csproj -t:Deploy   # also copies the mod into GameData
```

The KSP install is found through `KSPRoot` in `Source/Directory.Build.props`; override it in a
`Source/KSPRoot.user.props` file.

`KerbalRelations.Core` is the simulation with no Unity or KSP references. Its sources are compiled into
the one plugin DLL.
