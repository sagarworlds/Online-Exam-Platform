import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminSection } from '../../auth/admin-sections';
import { AdminSidebar } from './admin-sidebar';

const sections: AdminSection[] = [
  { label: 'admin.area.questions.name', description: 'admin.area.questions.description', path: '/admin/questions', permission: 'question.manage', icon: 'question' },
  { label: 'admin.area.invites.name', description: 'admin.area.invites.description', path: '/invites', permission: 'invite.manage', icon: 'invite' },
];

describe('AdminSidebar', () => {
  let fixture: ComponentFixture<AdminSidebar>;
  let root: HTMLElement;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [AdminSidebar],
      providers: [provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminSidebar);
    fixture.componentRef.setInput('sections', sections);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  });

  it('renders one link per section, labelled and reachable', () => {
    const links = Array.from(root.querySelectorAll('.admin-sidebar__link')) as HTMLAnchorElement[];

    expect(links.map((a) => a.querySelector('.admin-sidebar__label')?.textContent?.trim())).toEqual(['Questions', 'Invites']);
    expect(links[0].getAttribute('href')).toBe('/admin/questions');
    expect(links[1].getAttribute('href')).toBe('/invites');
  });

  it('starts expanded', () => {
    expect(root.querySelector('.admin-sidebar--collapsed')).toBeNull();
    expect(root.querySelector('button')!.getAttribute('aria-expanded')).toBe('true');
  });

  it('collapsing keeps every link reachable and labelled, just without the visible text', () => {
    root.querySelector('button')!.click();
    fixture.detectChanges();

    expect(root.querySelector('.admin-sidebar--collapsed')).not.toBeNull();
    const links = Array.from(root.querySelectorAll('.admin-sidebar__link')) as HTMLAnchorElement[];
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/admin/questions', '/invites']);
    expect(links.map((a) => a.getAttribute('aria-label'))).toEqual(['Questions', 'Invites']);
    expect(links.map((a) => a.getAttribute('title'))).toEqual(['Questions', 'Invites']);
    expect(root.querySelector('button')!.getAttribute('aria-expanded')).toBe('false');
  });

  it('remembers the collapsed state for the next render', () => {
    root.querySelector('button')!.click();
    fixture.detectChanges();

    const second = TestBed.createComponent(AdminSidebar);
    second.componentRef.setInput('sections', sections);
    second.detectChanges();

    expect((second.nativeElement as HTMLElement).querySelector('.admin-sidebar--collapsed')).not.toBeNull();
  });
});
