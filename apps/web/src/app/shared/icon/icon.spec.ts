import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Icon, IconName } from './icon';

describe('Icon', () => {
  async function render(name: IconName): Promise<ComponentFixture<Icon>> {
    await TestBed.configureTestingModule({ imports: [Icon] }).compileComponents();
    const fixture = TestBed.createComponent(Icon);
    fixture.componentRef.setInput('name', name);
    fixture.detectChanges();
    return fixture;
  }

  const names: IconName[] = ['edit', 'trash', 'restore', 'refresh', 'language', 'chart', 'folder', 'upload', 'download', 'transfer', 'filter', 'plus', 'close', 'pin', 'search', 'choice', 'text', 'chevron-down', 'chevron-up'];

  it.each(names)('draws something for %s', async (name) => {
    const fixture = await render(name);

    const svg = (fixture.nativeElement as HTMLElement).querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg!.children.length).toBeGreaterThan(0);
  });

  it('is purely decorative: the svg is hidden from assistive technology', async () => {
    const fixture = await render('edit');

    expect((fixture.nativeElement as HTMLElement).querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });
});
