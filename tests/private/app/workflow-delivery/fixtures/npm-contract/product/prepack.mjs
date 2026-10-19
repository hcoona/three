import { writeFile } from 'node:fs/promises';

await writeFile('dist/prepacked.txt', 'prepack ran\n');
