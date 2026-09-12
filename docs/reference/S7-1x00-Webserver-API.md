# S7-1x00 Webserver API

Nothing to do with Openness. This is the JSON-RPC service an S7-1200 / S7-1500 exposes on its own web server, and everything here was **measured against two real CPUs** rather than taken from documentation — including one finding that makes a bulk read thirty times faster than the reference implementation manages.

Useful on its own if you are talking to an S7 web server from anything at all.

## The CPU web server API

Nothing to do with Openness: this is the JSON-RPC service an S7-1200 / S7-1500 exposes on its own web server, and it is how the satellites read live values. Everything below was measured against two real CPUs — an S7-1500 at `Api.Version 2.00906` and an S7-1200 G2 at `Api.Version 6` — not taken from documentation.

Single endpoint, `https://<ip>/api/jsonrpc`. `Api.Login` returns a token that rides in the `X-Auth-Token` header. The certificate is self-signed and TLS 1.2 is required, so both settings belong on the `HttpClientHandler` and **not** on `ServicePointManager`, whose equivalents would change every connection the process makes.

### Batching: the finding that mattered

`PlcProgram.Read` documents a list in `var`. **Both CPUs reject it** with `-32602 Invalid Params`. The reference Python application tries only that, gives up, and falls back to one HTTP request per variable.

But JSON-RPC 2.0's own batching — an *array of envelopes* in one POST — works on both:

| Reading 54 variables |  |
| --- | --- |
| one request per variable | 4,671 ms |
| one batched POST | **150 ms** |

Thirty-one times. Extrapolated to a block of 5,000 variables that is seven minutes against fourteen seconds. **Results are matched by request `id`, never by position**: the specification lets a server answer a batch in any order, and both CPUs happening to keep the order is not something to build on. A batch also carries a per-entry `error`, so one unreadable variable no longer poisons the other forty-nine.

### Browse is not a cheap call, and batching does not help it

On the S7-1200 G2, `PlcProgram.Browse` costs a **flat ~92 ms per call whatever the batch size** — 1 call or 800, the per-call cost does not move. The work is CPU-side, not round trips. The only remedy is fewer calls.

Hence the array optimisation: **every element of an S7 array has the same type, so only the first is browsed and its subtree is copied onto the rest.** The first and last element are both browsed and their members compared before copying, on every array of every run — the guarantee comes from the language, but a capture is worth no more than its weakest assumption.

| Browsing a 1,614-variable block | S7-1500 | S7-1200 G2 (8,910 vars) |
| --- | --- | --- |
| depth-first, one call per node | 7,767 ms | 96,405 ms |
| batched per tree level | 1,611 ms | 96,405 ms |
| plus array-template copying | **182 ms** | **887 ms** |

### Reading scales with the block, not with the CPU

|  | S7-1500 | S7-1200 G2 |
| --- | --- | --- |
| 500 variables | 2.88 ms/var | 3.65 ms/var |
| 1,614 variables | 2.94 ms/var | 3.63 ms/var |
| a block of ~590 variables present on both | 1,908 ms | 2,231 ms |

Flat per variable, and the two CPUs are within 26 % of each other on the same block. A capture taking 33 s instead of 5 s is a bigger block, not a slower processor. Raising the read batch from 50 to 400 buys 10 %, all of it before 100, so 50 stays — a smaller batch is a smaller thing to lose when a POST fails.

### What the API returns, and what it refuses

The default mode hands back **decoded, natively typed JSON** — no hex, no strings for everything:

```
time    0            real  6.785        bool  false
int     15  /  -1    udint 422690608    string "sin valor"
```

`mode: "raw"` returns the S7 memory image instead, big-endian: `6.785` becomes `[64,217,30,184]`, and a STRING arrives as `[max, length, chars..., padding]`. Useful only for a type the CPU cannot convert itself.

Two refusals are worth knowing, because they shape the walk:

| Call | Answer |
| --- | --- |
| browse or read an array without an index | `203 Invalid array index` |
| read a whole struct, or a DTL, in one call | `204 Unsupported address` |

The second one closes off the obvious optimisation: there is no reading a struct in one request, so the walk has to reach every leaf. The first is why arrays are checked **before** `has_children` — an array of structs reports children too, and descending into it without an index is an error rather than a descent.

### What the two CPUs do not share

|  | S7-1500 | S7-1200 G2 |
| --- | --- | --- |
| `Api.Version` | 2.00906 | 6 |
| Methods exposed | 31 | 82 |
| Extra families | — | `Modules.*`, `Failsafe.*`, `Technology.*`, `Plc.ReadCpuType`, `Plc.ReadSystemTime` |

None of the extra methods offers a bulk read: JSON-RPC batching is the best available on both.
