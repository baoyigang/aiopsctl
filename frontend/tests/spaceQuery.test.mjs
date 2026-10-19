import assert from 'node:assert/strict';
import test from 'node:test';
import { resolveSpaceQuery, spaceQuery } from '../src/spaceQuery.ts';

const spaces = Object.freeze([
  Object.freeze({ id: 'default', name: '默认空间' }),
  Object.freeze({ id: 'special', name: ' 研发 + & / % # 空间 ' }),
  Object.freeze({ id: 'literal', name: '%E9%BB%98%E8%AE%A4' }),
]);

function expectSpace(query, expected, preferredId) {
  assert.deepEqual(resolveSpaceQuery(spaces, query, preferredId), { space: expected, error: '' });
}

function expectError(query, pattern, available = spaces) {
  const result = resolveSpaceQuery(available, query, 'default');
  assert.equal(result.space, null, 'Invalid explicit selectors must never select a fallback space');
  assert.match(result.error, pattern);
}

test('matches decoded Unicode names exactly', () => {
  expectSpace({ space: '默认空间' }, spaces[0]);
});

test('keeps spaces, plus, ampersand, slash, percent, and hash in names', () => {
  expectSpace({ space: spaces[1].name }, spaces[1]);
  expectError({ space: spaces[1].name.trim() }, /未知.*名称/);
  expectError({ space: ' 默认空间 ' }, /未知.*名称/);
});

test('does not decode query names a second time', () => {
  expectSpace({ space: spaces[2].name }, spaces[2]);
  expectError({ space: encodeURIComponent(spaces[0].name) }, /未知.*名称/);
});

test('valid spaceId takes precedence over both valid and invalid names', () => {
  expectSpace({ spaceId: 'special', space: '默认空间' }, spaces[1]);
  expectSpace({ spaceId: 'special', space: '不存在' }, spaces[1]);
  expectSpace({ spaceId: 'special', space: ['默认空间', null] }, spaces[1]);
});

test('unknown spaceId does not fall back to a valid name or preferred space', () => {
  expectError({ spaceId: 'unknown', space: '默认空间' }, /未知.*ID/);
});

test('unknown name never selects another space', () => {
  expectError({ space: '不存在' }, /未知.*名称/);
});

test('empty and valueless name and ID selectors are rejected', () => {
  for (const value of ['', null]) {
    expectError({ space: value }, /不能为空/);
    expectError({ spaceId: value, space: '默认空间' }, /不能为空/);
  }
});

test('all array selectors are rejected, including one value, empty, and repeated values', () => {
  for (const value of [[], ['默认空间'], ['默认空间', '默认空间'], ['默认空间', null]]) {
    expectError({ space: value }, /一个空间|重复|数组/);
  }
  for (const value of [[], ['default'], ['default', 'special'], [null]]) {
    expectError({ spaceId: value, space: '默认空间' }, /一个空间|重复|数组/);
  }
});

test('duplicate names are ambiguous, while an explicit ID still resolves them', () => {
  const duplicates = [{ id: 'a', name: '重名' }, { id: 'b', name: '重名' }];
  expectError({ space: '重名' }, /重名|无法确定/, duplicates);
  assert.deepEqual(resolveSpaceQuery(duplicates, { space: '重名', spaceId: 'b' }), {
    space: duplicates[1], error: '',
  });
});

test('duplicate IDs cannot silently select the first match', () => {
  expectError({ spaceId: 'a' }, /重复|无法确定/, [
    { id: 'a', name: '甲' }, { id: 'a', name: '乙' },
  ]);
});

test('missing selectors use a valid preferred space, otherwise the first space', () => {
  expectSpace({}, spaces[1], 'special');
  expectSpace({}, spaces[0]);
  expectSpace({}, spaces[0], 'removed');
  expectSpace({ search: '关键词', page: '2' }, spaces[0]);
  expectSpace({ space: undefined, spaceId: undefined }, spaces[1], 'special');
});

test('no available spaces returns an explicit error', () => {
  expectError({}, /没有.*空间/, []);
  expectError({ space: '默认空间' }, /没有.*空间/, []);
});

test('spaceQuery writes the raw name, removes legacy ID, and preserves unrelated query values', () => {
  const tags = Object.freeze(['甲', null, '乙']);
  const input = Object.freeze({
    space: '默认空间', spaceId: 'default', tags, filter: 'a+b&c/#%', empty: null, omitted: undefined,
  });
  const snapshot = structuredClone(input);
  const result = spaceQuery(input, spaces[1]);

  assert.deepEqual(result, {
    space: spaces[1].name, tags: ['甲', null, '乙'], filter: 'a+b&c/#%', empty: null, omitted: undefined,
  });
  assert.equal(Object.hasOwn(result, 'spaceId'), false);
  assert.notEqual(result, input);
  assert.deepEqual(input, snapshot);
  assert.equal(input.spaceId, 'default');
});

test('query creation and resolution round trip without encoding or input changes', () => {
  const input = Object.freeze({ period: '1h' });
  for (const space of spaces) {
    const query = spaceQuery(input, space);
    expectSpace(query, space);
    assert.equal(query.space, space.name);
  }
  assert.deepEqual(input, { period: '1h' });
});

test('canonical query keeps the selected ID for duplicate names and round trips stably', () => {
  const duplicates = [{ id: 'a', name: '重名' }, { id: 'b', name: '重名' }];
  const selected = duplicates[1];
  const initial = Object.freeze({ spaceId: 'b', period: '1h' });
  const canonical = spaceQuery(initial, selected, duplicates);

  assert.deepEqual(canonical, { spaceId: 'b', space: '重名', period: '1h' });
  assert.deepEqual(resolveSpaceQuery(duplicates, canonical), { space: selected, error: '' });
  assert.deepEqual(spaceQuery(canonical, selected, duplicates), canonical);
  assert.deepEqual(initial, { spaceId: 'b', period: '1h' });

  // Changing an ambiguous selection must replace any previous ID.
  const changed = spaceQuery(canonical, duplicates[0], duplicates);
  assert.equal(changed.spaceId, 'a');
  assert.deepEqual(resolveSpaceQuery(duplicates, changed), { space: duplicates[0], error: '' });
});

test('metadata with unique names removes an obsolete ID from the canonical query', () => {
  const canonical = spaceQuery({ spaceId: 'default', page: '2' }, spaces[1], spaces);
  assert.deepEqual(canonical, { space: spaces[1].name, page: '2' });
  expectSpace(canonical, spaces[1]);
});

test('resolveSpaceQuery accepts frozen input and leaves it unchanged', () => {
  const query = Object.freeze({ space: '默认空间', search: '值' });
  expectSpace(query, spaces[0]);
  assert.deepEqual(query, { space: '默认空间', search: '值' });
});
