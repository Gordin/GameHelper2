# Targetable layout and guarded recovery, 2026-10-05

The inspected PoE2 image has SHA256
`9d9716f155fd87b206a458feeb04708230de1dd466f8681a156947d971841a93`.
An entity found through native awake/sleeping maps has a Targetable component owned by
that entity. Its vtable identifies these methods in the executable:

- Deleting destructor: RVA `0x1729880`, calls destructor `0x17298C0`.
- Destructor: at `0x1729C2E`, takes `this+0x50`; at `0x1729C32`, calls container cleanup
  `0x140D80`, leaving return address `0x1729C37`.
- Debug description method (vtable slot 13): RVA `0x172B470`.

The captured access-violation stack includes `0x1729894` and `0x1729C37`. The faulting
instruction at `0x140DA1` dereferences `RAX+8`, with `RAX=0x10100`. The previous writes
set bytes at 0x51 and 0x52 to 1; these addresses are inside the pointer at 0x50, not flags.
This strongly links the layout error to Targetable destruction. The dump did not capture
the corresponding heap object, so its contents cannot be directly checked retrospectively.

The client's own debug method prints these UTF-16 labels from `.rdata` and then loads
one byte from the indicated component offset:

| Label | Label RVA | Field offset |
| --- | --- | --- |
| Targetable | `0x33A86A8` | `0x69` |
| Hidden from Player | `0x33A8620` | `0x73` |
| Meets Quest State | `0x33A85F8` | `0x6E` |
| Meets Item Requirements | `0x33A8650` | `0x6F` |

PortalFlagReader recovers those four offsets from the live debug method rather than
assuming this image's RVAs. It reads the method and labels only within the main module,
rejects unknown prints, missing/duplicate labels, overlapping offsets, non-boolean values
and changed identities. It executes no game code and performs no writes. Two distinct
portal entities in the same game session were sampled with Targetable values 0 and 1,
respectively. Their differing identities prevent interpreting this as restoration.

The highlight/outline update method at RVA 0x1729590 tests byte this+0x6A at 0x172959A and returns if zero. Its following code selects render colors and changes the render outline bit. This identifies the highlight gate independently of the inherited framework layout. The reference sets targeting and highlighting to 1 once per entity/area; it does not recreate models or change server-side entry counts.

PortalRecoveryLayout matches SHA256 hashes of the 608-byte highlight method, 240-byte diagnostic method, 80-byte target predicate (0x172B6B0), and 9-byte vector-cleanup call site. It also requires Targetable vtable RVA 0x33A6028, matching named flag offsets, a valid supported portal with an owned Portal component, boolean values and satisfied conditions. PortalWriteGate allows only 0x69/0x6A. PortalAreaFence rechecks native loading, area and player identities before each write; the portal is validated again. Old-area addresses are discarded without undo. Unknown profiles cannot obtain a recovery writer.

Read-only live verification matched all four code hashes and the current closed portal's full identity. Offline tests cover field gates, area changes, recycled identities, unknown profiles and undo. Appearance, click handling and server re-entry await user testing; matching code or reading back bytes alone do not establish those outcomes. No game-memory writes were performed during this validation. Legacy offsets remain solely in test-owned allocations; arbitrary writable attachments reject foreign processes.


## Incomplete Steam discovery

A user-supplied Steam diagnostic matches the same instruction profile with recovery enabled, but has ScanComplete=false, 487 awake plus 483 sleeping nodes visited, no portal rows and no writes. This does not show an unsupported client or establish a Windows-version cause. The old collector restarted traversal on every sample and discarded discovered candidates if the subsequent observation pass had no remaining budget.

The v4 collector resumes using the last fully inspected entity key, seeks from the current root each time, alternates map priority, and retains discovered identities before optional diagnostic reads. Partial candidate parsing retries the same key. It keeps the shared 15 ms / 4,000-read budget, with 128 keys per map per sample; crossing 2,000 total nodes no longer prevents eventual completion of a valid tree. Tests cover a 2,503-entity map, budget boundaries, root/head changes, cancellation and cycles. A read-only live sweep completed 132 awake and 97 sleeping entities across five small pages and retained the portal throughout. The friend's manual result remains pending.


## Client update inspected on 2026-10-06

The installed executable SHA256 is `7157984534da1b146e9b0be342aa9fd24272915b6f478aadef9830fc7873f89f`.
The live export reports complete discovery and one valid resident closed portal, but the old profile is unsupported. Named diagnostic flags still identify target 0x69, hidden 0x73, quest 0x6E and item 0x6F.

- Targetable vtable: RVA `0x33A7338`; slot 0 deleting destructor `0x1729B10`, calls destructor `0x1729B50`.
- Debug method, slot 13: `0x172B700`; the named target print loads byte 0x69 at `0x172B74D`.
- Highlight update: `0x1729820`; tests byte 0x6A at `0x172982A`, returns when zero, then performs the render-color/outline update. Targetable's on-load method calls it at `0x172A036`.
- Target predicate: `0x172B940`; tests target 0x69 and the same visibility/requirement flags.
- Container cleanup: `0x1729EBE` takes this+0x50; `0x1729EC2` calls `0x140D80`. Legacy pointer fields remain forbidden.

A new explicit profile verifies all four instruction-block hashes. The October 5 profile remains available; matching blocks from different profiles cannot be combined. The selected profile's vtable is used for every portal identity validation and its name is exported. The new profile and current portal passed a live read-only check. No game-memory writes or clicks were performed during patch validation. Visual recovery/re-entry on the updated client await the user's test.

## Client update inspected on 2026-10-10

The installed executable SHA256 is `984e7bb2b503aef5fe26ac0cc4004eaae079396fb081cbe3bc39d945f190f117`.
The helper's diagnostic retained a valid closed portal with target 0, hidden 0 and satisfied quest/item flags, but reported an unsupported profile and no writes. A subsequent read-only live sample confirmed Targetable vtable RVA `0x33A7308`. Area changes during inspection invalidated earlier entity addresses; those were not reused.

The deleting destructor, debug method, highlight update, target predicate and vector-cleanup call site retain the October 6 RVAs. The debug method still identifies target `0x69`, hidden `0x73`, quest `0x6E` and item `0x6F` by name. The highlight method still tests `0x6A` at `0x172982A` before its render-color/outline update; the vector at `0x50` remains separate and must not be modified.

The new profile verifies these complete instruction blocks:

| RVA | Bytes | SHA256 |
| --- | --- | --- |
| `0x1729820` | 608 | `386623ad54cab674b53a898488de78c050484c5a7a88abfb9c23e9fb0a740082` |
| `0x172B700` | 240 | `20a12baf44b5bbbc1de7dabe6e76381457f883c667feea1aa3569e334fb6c278` |
| `0x172B940` | 80 | `65f64da3af977255ce016b950a36b889c211023be529e512d3742d6b258fdcfb` |
| `0x1729EBE` | 9 | `a497dc80926369a87e18abca88fde4fab76758fe7114de45ef0025081d2c893b` |

Both earlier profiles remain supported. The vtable regression test now checks every matching and mismatched pair across all three profiles. The whole solution build and 68 offline tests pass. Read-only inspection of the running client selects `poe2-2026-10-10-targetable-69-6a`; the post-build sample was in another area with no portal discovered, so closed-portal identity and recovery were not revalidated there. No game-memory writes or clicks were performed during patch validation; visual recovery and re-entry still require the user's test.
