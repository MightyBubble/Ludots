export interface StrokeCell {
    c: number;
    r: number;
}

/**
 * Cells strictly after `from` up to and including `to`, each one column or one row away from the previous.
 * Single-axis steps keep consecutive cells adjacent in both square and offset-hex topologies, so a fast
 * drag whose mouse samples skip cells still paints a gap-free stroke.
 */
export function walkStrokeCells(from: StrokeCell, to: StrokeCell, visit: (c: number, r: number) => void): void {
    const dc = Math.abs(to.c - from.c);
    const dr = Math.abs(to.r - from.r);
    const stepC = Math.sign(to.c - from.c);
    const stepR = Math.sign(to.r - from.r);
    let c = from.c;
    let r = from.r;
    let ic = 0;
    let ir = 0;
    while (ic < dc || ir < dr) {
        if (ir >= dr || (ic < dc && (0.5 + ic) * dr < (0.5 + ir) * dc)) {
            c += stepC;
            ic++;
        } else {
            r += stepR;
            ir++;
        }
        visit(c, r);
    }
}
