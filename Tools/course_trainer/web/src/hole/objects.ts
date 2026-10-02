// objects.bin: every tree, shrub and rock of the hole (contract: Docs/hole-format/README.md, section 5).
// The browser places exactly these, so the trainer shows what Unity builds; only the models differ.

/** Kind enum, in file order (append-only). */
export const OBJECT_KINDS = ['conifer', 'deciduous', 'palm', 'cactus', 'shrub', 'boulder', 'rock'] as const;
export type ObjectKind = (typeof OBJECT_KINDS)[number];

export interface PlacedObject {
  x: number;        // metres east
  north: number;    // metres north
  height: number;   // metres, ground to top
  radius: number;   // metres, collision footprint
  rotation: number; // radians clockwise from north
  variant: number;  // 0..1, picks the model / tint
}

export type ObjectsByKind = Record<ObjectKind, PlacedObject[]>;

const HEADER_BYTES = 16;
const MAGIC = 'GOBJ';

export function decodeObjects(buffer: ArrayBuffer, sizeMeters: number, expectedCount: number): ObjectsByKind {
  const view = new DataView(buffer);
  const magic = String.fromCharCode(...new Uint8Array(buffer, 0, 4));
  if (magic !== MAGIC) throw new Error(`objects.bin: bad magic '${magic}'`);
  const version = view.getUint16(4, true), recordSize = view.getUint16(6, true), count = view.getUint32(8, true);
  if (version !== 1) throw new Error(`objects.bin: version ${version}, expected 1`);
  if (count !== expectedCount || buffer.byteLength !== HEADER_BYTES + count * recordSize)
    throw new Error(`objects.bin: ${count} records / ${buffer.byteLength} bytes don't match hole.json (${expectedCount})`);

  const out = Object.fromEntries(OBJECT_KINDS.map(k => [k, [] as PlacedObject[]])) as ObjectsByKind;
  for (let i = 0, o = HEADER_BYTES; i < count; i++, o += recordSize) {
    const kind = OBJECT_KINDS[view.getUint8(o)];
    if (!kind) continue; // newer kind than this viewer knows: skip
    out[kind].push({
      variant: view.getUint8(o + 1) / 256,
      rotation: (view.getUint8(o + 2) / 256) * Math.PI * 2,
      x: (view.getUint16(o + 4, true) / 65535) * sizeMeters,
      north: (view.getUint16(o + 6, true) / 65535) * sizeMeters,
      height: view.getUint16(o + 8, true) / 100,
      radius: view.getUint16(o + 10, true) / 100,
    });
  }
  return out;
}
