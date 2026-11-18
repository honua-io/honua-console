import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
    delimitedObjects,
    delimitedRows,
    useCodeFreeDelimitedReaders,
} from '../../src/Honua.Console.Shell/wwwroot/chart-preview.js';

test('delimited rows follow RFC 4180 quoting and line endings', () => {
    assert.deepEqual(delimitedRows('a,b\r\n"q,1","he said ""hi"""\n"m\nn",\n', ','), [
        ['a', 'b'], ['q,1', 'he said "hi"'], ['m\nn', ''],
    ]);
    assert.deepEqual(delimitedRows('', ','), []);
    assert.deepEqual(delimitedRows('a\n\nb', ','), [['a'], [''], ['b']]);
});

test('a quote inside an unquoted field is literal, as in d3-dsv', () => {
    assert.deepEqual(delimitedRows('name,size\npipe,12" long', ','), [['name', 'size'], ['pipe', '12" long']]);
    assert.deepEqual(delimitedRows('"a"b,c', ','), [['ab', 'c']]);
    assert.throws(() => delimitedRows('a::b', '::'), /invalid delimiter/);
});

test('delimiters that are special inside a character class still split', () => {
    for (const delimiter of ['\t', '|', ']', '-', '^', '\\']) {
        assert.deepEqual(delimitedRows(`a${delimiter}b\n1${delimiter}2`, delimiter), [['a', 'b'], ['1', '2']]);
    }
});

test('objects use the header row or format.header and fill short rows like d3-dsv', () => {
    assert.deepEqual(delimitedObjects('a,b\n1,2\n3', { delimiter: ',' }), [{ a: '1', b: '2' }, { a: '3', b: '' }]);
    assert.deepEqual(delimitedObjects('1,2', { delimiter: ',', header: ['x', 'y'] }), [{ x: '1', y: '2' }]);
    const [row] = delimitedObjects('__proto__\npolluted', { delimiter: ',' });
    assert.equal(Object.getPrototypeOf(row), Object.prototype);
    assert.equal(Object.hasOwn(row, '__proto__'), true);
});

test('the readers replace Vega csv, tsv and dsv parsing and read text', () => {
    const registered = {};
    useCodeFreeDelimitedReaders({ formats: (name, reader) => { registered[name] = reader; } });
    assert.deepEqual(Object.keys(registered), ['csv', 'tsv', 'dsv']);
    assert.ok(Object.values(registered).every(reader => reader.responseType === 'text'));
    assert.deepEqual(registered.csv('a,b\n1,2'), [{ a: '1', b: '2' }]);
    assert.deepEqual(registered.tsv('a\tb\n1\t2', { type: 'tsv', delimiter: ',' }), [{ a: '1', b: '2' }]);
    assert.throws(() => registered.dsv('a;b'), /invalid delimiter/);
    assert.deepEqual(registered.dsv('a;b\n1;2', { type: 'dsv', delimiter: ';' }), [{ a: '1', b: '2' }]);
    assert.doesNotThrow(() => useCodeFreeDelimitedReaders({}));
});
