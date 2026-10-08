import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminIcon, AdminIconName } from './admin-icon';

describe('AdminIcon', () => {
  async function render(name: AdminIconName): Promise<ComponentFixture<AdminIcon>> {
    await TestBed.configureTestingModule({ imports: [AdminIcon] }).compileComponents();
    const fixture = TestBed.createComponent(AdminIcon);
    fixture.componentRef.setInput('name', name);
    fixture.detectChanges();
    return fixture;
  }

  const names: AdminIconName[] = ['question', 'book', 'exam', 'attempt-request', 'dispute', 'invite', 'batch', 'guardian', 'otp', 'whatsapp'];

  it.each(names)('draws something for every icon the admin sections use (%s)', async (name) => {
    const fixture = await render(name);

    const svg = (fixture.nativeElement as HTMLElement).querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg!.children.length).toBeGreaterThan(0);
  });

  it('is purely decorative: the svg is hidden from assistive technology', async () => {
    const fixture = await render('question');

    expect((fixture.nativeElement as HTMLElement).querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });
});
