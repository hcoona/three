import { strict as assert } from 'node:assert';
import { smokeMessage } from './dist/index.js';

assert.equal(smokeMessage(), 'hcoona-release-smoke-npm');
