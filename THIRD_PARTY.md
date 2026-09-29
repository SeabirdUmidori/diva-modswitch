# Third-party components

## DivaModLoader - `payload/dinput8.dll`

This repository compiles one file that is **not our work** into the shipped `.exe`:

| | |
|---|---|
| File | `payload/dinput8.dll` (embedded as the manifest resource `modswitch.dinput8.dll`) |
| Size | 551 424 bytes |
| MD5 | `1a3705451fb2dea523cb45bcbca1ee11` |
| SHA-256 | `462c31741e72367e56144a4045a64a7713631e02e586d5195bb24f07ba972974` |
| Project | [DivaModLoader](https://github.com/blueskythlikesclouds/DivaModLoader) by Skyth |
| License | MIT - full text kept at [THIRD_PARTY/DivaModLoader/LICENSE.md](THIRD_PARTY/DivaModLoader/LICENSE.md) |
| Modified? | **No.** The bytes are exactly as downloaded; verify with the hashes above. |

The file carries **no version metadata at all** (`FileVersion`, `ProductVersion`, `CompanyName` are all
empty in its PE version resource), which is why identity here is stated as a hash rather than a version
number. If you need to know which release it came from, compare the hash against the files on
DivaModLoader's own Releases page - that page stays authoritative, and this repository does not claim
otherwise.

### Why it is compiled in

The tool's *Install mod loader* button exists so that a machine with mods on disk but no loader yet can
be made to load them in one click. Shipping the loader inside the single `.exe` keeps the promise that
nothing has to be downloaded or installed.

### Why it never gets in your way

The precedence order in `InstallLoaderCore()` is deliberate:

1. a `loader\dinput8.dll` **you** put next to the exe is used as-is;
2. otherwise, if the game folder already has a `dinput8.dll`, that copy is kept and saved into
   `loader\` - so a newer loader you installed yourself is never downgraded;
3. only if neither has anything does the compiled-in copy get written out;
4. and if the game folder already has a `dinput8.dll` of a different size, the tool says so and **leaves
   the file alone**.

The self-test asserts rules 3 and 4 (`modswitch-test.cs`, step 16).

### Removing it from your build

Delete `payload/dinput8.dll` and drop the `set PAYLOAD=` line from `build.bat`: the compiler emits no
resource, `BundledLoader()` returns `null`, and the button simply tells the user to supply the loader
instead of installing it. The rest of the tool is unaffected.
